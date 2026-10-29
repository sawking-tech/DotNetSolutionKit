# NamespaceRoot.ProductName

Rules for an AI agent working in this solution. The solution was generated from
[DotNetSolutionKit](https://dotskit.sawking.tech/); the mechanisms it ships are described in
[its documentation](https://dotskit.sawking.tech/docs.html), and every link below points there.

This file is committed: no tokens, keys, credentials or personal data in it.

## Layout

```
.dotskit/manifest.json              # what the solution was generated from, kept by dotskit and committed
deploy/                             # the infrastructure and the services for docker compose or Kubernetes (with --Deploy compose or k8s)
tests/servers/                      # a script per test server (start, ready, logs, down); up.sh starts them all
src/
├── Directory.Packages.props        # every package version, declared once here or in package-versions/ (central package management)
├── package-versions/               # versions grouped by what needs them (the database, MongoDB, email...), imported by Directory.Packages.props
├── common/                         # shared by every service
│   ├── NamespaceRoot.ProductName.Common                 # domain kernel: Entity, AggregateRoot, events, specifications, exceptions
│   ├── NamespaceRoot.ProductName.Common.Contracts       # what crosses a service boundary: routes, requests, responses, bus messages
│   ├── NamespaceRoot.ProductName.Common.Application     # domain event pipeline, idempotency, permissions, messaging abstractions
│   ├── NamespaceRoot.ProductName.Common.Infrastructure  # EF Core base classes, interceptors, repositories, schema guard, MassTransit
│   ├── NamespaceRoot.ProductName.Common.Web             # web pipeline: errors, validation, Swagger, authentication, permissions
│   └── NamespaceRoot.ProductName.Common.Testing         # test infrastructure for the services' tests
├── capabilities/                   # a project per capability the solution uses (email, MongoDB), referenced by the services that use it
└── services/
    ├── NamespaceRoot.ProductName.All.sln                # every project; the entry point
    └── NamespaceRoot.ProductName.<Service>/
        ├── NamespaceRoot.ProductName.<Service>                 # domain: entities, aggregates, events, policies
        ├── NamespaceRoot.ProductName.<Service>.Application     # use cases, event handlers, ports
        ├── NamespaceRoot.ProductName.<Service>.Infrastructure  # DbContext, configurations, repositories, jobs, consumers
        ├── NamespaceRoot.ProductName.<Service>.API             # controllers, Program, host setup
        └── NamespaceRoot.ProductName.<Service>.Tests
```

Each project references only what it lists in its `.csproj` (`DisableTransitiveProjectReferences`), so
crossing a layer is a compile error: [projects and layers](https://dotskit.sawking.tech/docs.html#layers).
The API project does not see the domain; it goes through the application layer.

Each service owns a database schema named after it and refuses to start on a schema another service owns:
[persistence](https://dotskit.sawking.tech/docs.html#persistence).

## Skills

Skills live in `.claude/skills/`. Apply them without waiting for `/skill-name`, and say which one you apply
before writing code, so the user can stop you if it is the wrong one.

| Work | Skill |
|------|-------|
| A new service, or a flag for an existing service | `/scaffold-service` |
| An entity or aggregate | `/add-entity` |
| A repository | `/add-repository` |
| Filtering, search | `/add-specification` |
| A domain event and its handlers | `/add-domain-event` |
| A service class (use case) | `/add-service-class` |
| A controller or endpoint | `/add-controller` |
| Tests | `/add-tests` |
| An EF migration | `/ef-migration` |
| A design choice before non-trivial code | `/pick-pattern` |
| Your own commits before a push | `/self-review` |
| Someone else's pull request | `/code-review` |
| Work that spans sessions | `/plan-and-iterate` |

A review of a service class, a repository or a specification uses the review flags of the matching skill.

## Commands

```bash
# Build and test everything
dotnet build src/services/NamespaceRoot.ProductName.All.sln
dotnet test src/services/NamespaceRoot.ProductName.All.sln

# Without the tests that need real servers (PostgreSQL, RabbitMQ and the rest)
dotnet test src/services/NamespaceRoot.ProductName.All.sln --filter "TestCategory!=Integration"

# The test servers of the solution, a script per part in tests/servers/, and the integration tests on them
eval "$(bash tests/servers/up.sh)"
dotnet test src/services/NamespaceRoot.ProductName.All.sln --filter "TestCategory=Integration"
bash tests/servers/up.sh down

# One service
dotnet test src/services/NamespaceRoot.ProductName.<Service>/NamespaceRoot.ProductName.<Service>.sln
```

Migrations: `/ef-migration`. A new service or a flag: `/scaffold-service`.

```bash
# A newer version of the template, with the team's changes kept: dotskit (dotnet tool install -g SawKing.DotsKit.Tool).
# Without --yes it lists the changes and writes nothing: show them to the user, then run it with --yes on their yes.
# A clean working tree; a solution without .dotskit/manifest.json runs dotskit init first, the same way.
dotskit upgrade
```

## Conventions

- Code, comments, XML docs, commit messages: English.
- A change in behaviour comes with its test in the same commit; `git checkout <sha>` builds and passes.
- Errors are exceptions from `Common/Exceptions`; the shared handler turns them into RFC 9457 problems:
  [errors](https://dotskit.sawking.tech/docs.html#errors).
- Time comes from `IDomainExecutionContext.TimeProvider`, never `DateTime.UtcNow`.
- Side effects of a change (a bus message, a job) go into domain event handlers, never inline in a
  service: [domain events](https://dotskit.sawking.tech/docs.html#domain-events).
- A key added to an `appsettings*.json` gets `"_comment_<Key>"` right after it: what goes there, and
  `REQUIRED` first when the service does not start without it. A word about the section rather than one key
  (defaults, where credentials come from) is `"_note_<Topic>"`. A key starting with `_` is metadata, which
  the options binder ignores: [JSON metadata keys](https://dotskit.sawking.tech/docs.html#adr-006).
- Configuration is read through typed options validated at startup, never `IConfiguration["Key"]` in a
  service: [settings](https://dotskit.sawking.tech/docs.html#settings).
- Before writing a file of a kind the solution already has (an entity configuration, a validator, a
  consumer), open two or three neighbours of that kind and follow their conventions.

## Plans

Work that does not finish in one go keeps its plan in `.claude/session-context/`, which `.gitignore` keeps
out of the repository: `/plan-and-iterate`.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
