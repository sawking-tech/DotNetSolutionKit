# Web layer

The host of every service is built from three calls in `Common.Web`, so the pipeline is the same in all
services and a fix to it reaches them with a `Common` update.

| Call | Registers or adds |
|---|---|
| `builder.AddPlatformLogging()` | Serilog from the `Serilog` section, with the version and the module on every line; health probes kept out of the log |
| `builder.AddPlatformWebApi(serviceAssembly, mvc => ...)` | [JSON](#json), [errors](errors.md), controllers with the [permission check](authentication-and-permissions.md), [validation](validation-and-pagination.md), Swagger, CORS from the `Cors` section, the execution context |
| `app.UsePlatformPipeline(serviceAssembly, authenticate, beforeEndpoints)` | the middleware, in the order below, and the endpoints |

## JSON

Every service serializes with `PlatformJson` from `Common.Application`: the controllers, minimal APIs,
`/health` and `/ready`, and any code that writes JSON itself through `PlatformJson.Options`.

| Value | On the wire |
|---|---|
| Property names | camelCase |
| `null` | left out |
| Enums | names: `"ApiKey"`, not `1` |
| `DateTimeOffset` | UTC with `Z`, fractional seconds only when present: `"2026-01-19T12:30:00Z"`; an offset read in is converted to UTC |
| Non-ASCII text | as is, not as `\uXXXX` escapes |

A number for an enum means nothing in a log, and reordering the enum changes the meaning of numbers
already stored or sent. A timestamp in the server's own offset makes two services describe one instant
differently, and clients comparing the strings get it wrong across a daylight-saving change.

## The pipeline

```mermaid
flowchart LR
    R([request]) --> FWD[Forwarded headers] --> COR[Correlation id] --> LOG[Request log] --> ROUTE[Routing] --> CORS --> ERR[Errors as problems] --> AUTH[Authentication and authorization] --> SW[Swagger] --> OWN["beforeEndpoints:<br/>the service's own"] --> EP["Controllers,<br/>/health, /ready"]
```

1. Forwarded headers: the client address, scheme, host and path base from `X-Forwarded-For`, `-Proto`,
   `-Host` and `-Prefix`, as the proxy in front received the request. First, so the log, the cookies,
   anything that checks for HTTPS and the links the service builds see the client's request, not the
   proxy's. See [behind a proxy](#behind-a-proxy).
2. Correlation: reads or creates `X-Correlation-Id`, puts it on every log line of the request and on the
   response. See [correlation](#correlation).
3. Request logging: one line per request with method, path, status and duration. It comes after
   correlation, so the line carries the identifier.
4. Routing.
5. CORS. Before error handling, so an error reaches a browser with CORS headers instead of as an opaque
   network failure.
6. Error handling: exceptions and empty 4xx/5xx responses become [problems](errors.md).
7. Authentication and authorization. After error handling, so their failures are problems too.
8. Swagger.
9. The service's own middleware, from `beforeEndpoints`: the Hangfire dashboard, for instance.
10. Controllers, `/health` and `/ready`.

## Correlation

The correlation identifier ties one external request to everything it caused. A caller may send its
own in `X-Correlation-Id`; without one the request takes its trace id. Searching the logs of every
service by that identifier finds the request, the messages it published, their consumers, the jobs
they enqueued and the calls to other services, each line under the `CorrelationId` property:

| Where the work goes | How the identifier gets there |
|---|---|
| a message | the `X-Correlation-Id` header of the message, put back for the consumer ([messaging](../features/messaging.md)) |
| a Hangfire job | a job parameter, put back while the job runs ([background jobs](../features/background-jobs.md)) |
| another service | the `X-Correlation-Id` header of the call ([calling another service](authentication-and-permissions.md#calling-another-service)) |

The trace (`traceparent`) crosses the same boundaries. The identifier travels separately because a caller
that sent its own searches by it, and the trace does not carry it past the first service.
Inside the process the identifier of the running work is `Correlation.Current`.

## Behind a proxy

By default the forwarded headers are accepted from any address: in containers the proxy's address
changes, and the deployment files publish a service on the host's loopback only, so nothing but the
proxy reaches it. A service reachable from elsewhere lists its proxies, and the headers from any other
address are ignored:

| Setting | |
|---|---|
| `ForwardedHeaders:KnownProxies` | addresses of the proxies, `["10.0.0.2"]` |
| `ForwardedHeaders:KnownNetworks` | their networks, `["10.1.0.0/16"]` |
| `ForwardedHeaders:ForwardLimit` | how many proxies the request passes through, 1 by default |
| `ForwardedHeaders:AllowedHosts` | the public names `X-Forwarded-Host` may carry, `["api.example.com", "*.example.com"]`; empty takes any |

The host and the path base are what a service builds its links from: the `Location` of a 201, a redirect.
Without them a link points at the service's internal address. A forwarded host outside
`AllowedHosts` is not taken, and the request keeps its own `Host`; the other headers still apply.

The proxy in front of the gateway, or of a service with no gateway, is where the client's request ends,
so it sets these headers itself: `X-Forwarded-Host` from the `Host` it received, and it removes an
`X-Forwarded-Prefix` the client sent. Otherwise a client chooses the host and the path of the service's
links. The edge of `deploy/compose/bluegreen.sh` does both; an ingress or a TLS proxy of your own must
too.

## Swagger documents

The version of an endpoint is the `api/v{n}` segment of its route. Swagger shows one document per version
found on the service's controllers, `v1`, `v2`, `v10` in numeric order, and an `all` document with every
endpoint. A route without a version, such as `internal/jobs`, is listed only in `all`. The
[API diff](../features/api-diff.md) compares the `all` document.

A string property with a closed set of values names where the values live, and the schema lists them:

```csharp
[SchemaValuesFrom(typeof(OrderStatuses), nameof(OrderStatuses.All))]
public string Status { get; init; }
```

The member is a static list of strings, or a dictionary whose keys are the values. The property stays a
string on the wire; the document gains its `enum` from the same list the code checks against.

Two settings shape what a reader of the document gets, on a service and on the gateway alike, and both
are checked at startup:

```json
"Swagger": {
  "PublicServers": [ { "Url": "https://api.example.com", "Description": "Production" } ],
  "ScrubPatterns": [ "\\(?\\bPROJ-\\d+\\b\\)?" ]
}
```

- `PublicServers` become the document's `servers`, so a client that imports it, such as Postman, knows
  where to send requests. Each is an absolute URL, scheme included; a host with a server variable instead
  was refused by Postman when it imported the document as OpenAPI 3.0. None means no `servers`, and the
  base URL is wherever the document came from: right for a local run.
- `ScrubPatterns` are regular expressions removed from every description of the document. An XML comment
  may name the task a decision came from, and Swashbuckle copies it word for word; the reader of the
  contract gets the prose without references to a tracker they cannot open, and the code keeps them.

## What a service keeps

```csharp
// SchemaHost.Build
builder.AddPlatformLogging();
builder.Configuration.SetupAppConfiguration(builder.Environment, args);
builder.AddPlatformWebApi(typeof(SchemaHost).Assembly);
builder.SetupAppServices()          // its own registrations
    .SetupHealthChecks();           // its database, its jobs
builder.SetupAppAuthentication();   // its authentication handler

// Program
var app = SchemaHost.Build(args);
// migrations and seeding
app.UsePlatformPipeline(typeof(Program).Assembly, beforeEndpoints: pipeline => pipeline.UseAppHangfire());
app.Run();
```

Configuration, the service's own registrations, health checks, the authentication handler and the
database setup stay in the service; everything else is shared. `SchemaHost.Build` describes the
application once, for `Program` and for the [API document](../adr/003-api-schema-generation.md).

The DI container is validated when it is built, in every environment: a missing registration or a scoped
service taken from the root fails the start, not the first request that needs it.

## Tests

`Common.Tests` builds a host with `AddPlatformWebApi` and `UsePlatformPipeline` on a test server and
checks health, an unknown route answered as a problem with the correlation identifier, and CORS for a
configured origin.
