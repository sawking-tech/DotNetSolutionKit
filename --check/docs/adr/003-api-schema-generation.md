# ADR-003: The API document is read from the built application

**Status:** Accepted, 2026-10-03

**Author:** Vladimir Savkin, the creator of DotNetSolutionKit

## Context

The [API diff](../features/api-diff.md) compares the OpenAPI document of a service on both sides of a
pull request, so it needs each service's document produced in CI, without a database, a broker or the
secrets a build agent does not hold.

One property drives every option: **producing an OpenAPI document means building the host.** The document
comes from `ApiExplorer`, which exists only once routing, conventions and the operation filters are wired
up. It cannot be read out of a compiled assembly without constructing the application.

Building the application had side effects. Migrations and seeding run between `Build()` and `Run()`, and
the schema guard checks the database while services are registered, so asking a service what its API
looks like touched a database. Two more facts had to be handled:

- `WebApplication` adds the authentication middleware itself as soon as it finds authentication schemes
  registered. Leaving it out of the pipeline changes nothing: the handlers still resolve services that are
  not registered, and every request fails. Authentication has to stay out of the container.
- Settings validated on start (API keys, secrets, public URLs) are checked before the host serves
  anything, and a build agent holds none of them.

## Decision

- **Schema-only mode** (`--schema-only` or `SCHEMA_ONLY=1`) builds the service without its infrastructure:
  it turns off every infrastructure switch, registers no infrastructure services, no authentication and no
  secret store, and drops startup validation of options.
- **`SchemaHost.Build(args)`** in each service constructs the application exactly as `Program` does and
  stops there. `Program` builds through the same method, so the document always describes the application
  that runs.
- **`SchemaDump.TryWrite`** in `Common.Web` writes the requested document straight out of the built
  container (`--dump-schema <file> --document <name>`) and tells `Program` to end the run.
- **`scripts/generate-api-schemas.sh`** runs every service that way and normalises the result with
  `scripts/normalise_api_schema.py`: the environment in the title, the version, server URLs and line
  endings from the machine that built the XML documentation would otherwise make two identical contracts
  differ.

## Alternatives considered

Each was tried in a product built on the template before this decision.

**1. A build-time target, `Microsoft.Extensions.ApiDescription.Server`.** It runs the assembly during the
build and reads the document from the host it starts. On a Linux build agent the process had already
disposed its service provider when the tool asked for the document, and the build failed with
`ObjectDisposedException`. The tool owns the lifetime of the host, so the failure could not be fixed from
the service.

**2. The Swashbuckle CLI, `dotnet swagger tofile`.** It works without a port: it builds the host from a
`SwaggerHostFactory` class in the assembly. But the CLI is an external tool whose runtime has to match
the service's: version 9 needs the .NET 9 runtime, so on .NET 8 it runs only with
`DOTNET_ROLL_FORWARD=LatestMajor`. And every service needs a factory class for it.

**3. Start each service and fetch the document over HTTP.** It needs a port, a readiness wait and a
process to kill, and each can fail. It failed without a sign: the script stopped `dotnet run`, which is
only a launcher, so the service kept its port; the next service could not bind, and the request was
answered by the one still running. Every document came back as the first service's, and the run reported
success.

## Risks and how they are handled

| Risk | What happens | Handled by |
|---|---|---|
| Schema-only mode and the real start drift apart | The document describes an application that differs from the one that runs | One `SchemaHost.Build` for both; the mode only switches infrastructure off, it does not register anything of its own |
| A service fails to start in schema-only mode and exits quietly | No document, or an empty one | The service exits with code 1 and prints the exception; the script fails when the file is missing or empty |
| A new infrastructure dependency is not switched off | Generating the document needs that dependency | Its registration goes into the infrastructure layer, which schema-only mode does not register, or its switch is added to `SchemaOnlyMode` |
| Our own code instead of a tool | It is maintained by us | Three small classes; each alternative above failed when it was tried |

## Consequences

- No port, no readiness wait and no process to kill, so the failures of option 3 cannot happen.
- Producing the document needs no infrastructure and no secrets, which matches what a build agent has.
- The document describes the application that runs, because `Program` and the generator build it the
  same way.
- A document per service takes seconds.
