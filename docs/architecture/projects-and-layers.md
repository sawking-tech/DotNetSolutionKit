# Projects and layers

A generated solution has three parts: the shared `Common` projects under `src/common`, generated once; a
project per capability a flag brings under `src/capabilities`; and one folder per service under `src/services`.

Who references whom; an arrow points at the project referenced:

```mermaid
flowchart BT
    subgraph common [src/common]
        C[Common]
        CC[Common.Contracts] --> C
        CA[Common.Application] --> C
        CI[Common.Infrastructure] --> C
        CI --> CA
        CI --> CC
        CW[Common.Web] --> C
        CW --> CC
        CW --> CA
        CW --> CI
    end
    subgraph capabilities [src/capabilities]
        CN["Capabilities.Messaging.Notifications, with --Notify email"]
        CM["Capabilities.Persistence.Mongo, with --MongoDB"] --> C
        CM --> CC
        CCH["Capabilities.Analytics.ClickHouse, with --ClickHouse"] --> C
        CCH --> CC
        CS["Capabilities.Persistence.S3, with --Storage"] --> C
    end
    subgraph service ["src/services/#lt;Service#gt;"]
        D["#lt;Service#gt;"] --> C
        A["#lt;Service#gt;.Application"] --> D
        A --> CA
        A --> C
        A --> CC
        I["#lt;Service#gt;.Infrastructure"] --> D
        I --> A
        I --> C
        I --> CA
        I --> CI
        I --> CC
        I -.-> CN
        I -.-> CM
        API["#lt;Service#gt;.API"] --> A
        API --> I
        API --> CW
    end
    subgraph gateway ["src/services/#lt;Gateway#gt;, with --ApiGateway"]
        G["#lt;Gateway#gt;.API"] --> CW
    end
```

## Common

| Project | Holds | References |
|---|---|---|
| `Common` | The domain kernel: `Entity`, `AggregateRoot`, domain events, `IUnitOfWork`, specifications, the user and execution context, exceptions | nothing |
| `Common.Contracts` | What crosses a service boundary: routes, request and response models, health constants | `Common` |
| `Common.Application` | Application abstractions: the domain event dispatcher, `IPermissionService`, messaging interfaces | `Common` |
| `Common.Infrastructure` | EF Core base classes, interceptors, repositories, migrations, the schema guard, messaging on MassTransit, security | `Common`, `Common.Application`, `Common.Contracts` |
| `Common.Web` | The web layer of a service: pipeline, errors, validation, Swagger, authentication, permissions, health | `Common`, `Common.Contracts`, `Common.Application`, `Common.Infrastructure` |
| `Common.Testing` | Test helpers tied to the audit pipeline: the audit verifiers and `TestCategories`; no test framework | all of the above, `Framework.Testing` |
| `Framework.Testing` | The portable testing harness: the test contexts, the real-database test servers of the solution's database (`PostgresDbTestExecutionContext`, `SqlServerDbTestExecutionContext`), `TestDatabase`, the stubs, the rule checks; no test framework | `Common`, `Common.Application`, `Common.Infrastructure`, `Framework.DomainEvents`, the provider module |
| `Common.Tests` | The tests of `Common` | all of the above, `Common.Testing` |
| `Framework.Auth` | The platform's authentication and authorization over JwtBearer: the composite API key or JWT scheme, the token cookie with its CSRF contract, the permission check | `Common`, `Common.Application`, `Common.Infrastructure` |
| `Framework.Auth.Tests` | The tests of authentication and permissions | `Framework.Auth` |
| `Framework.OpenApi` | The Swagger stack: the document setup, the filters, the schema dump of `--ApiDiff` | `Common`, `Common.Contracts` |
| `Framework.OpenApi.Tests` | The tests of the Swagger stack | `Framework.OpenApi` |
| `Framework.Validation` | The FluentValidation wiring over SharpGrip: the validators of the service's assembly and the 422 answer | nothing of `Common` |
| `Framework.Validation.Tests` | The tests of the validation wiring | `Framework.Validation` |
| `Framework.Observability` | The correlation identifier: the ambient value, the header name, the middleware that reads or creates it per request, the handler that puts it on an outgoing call | nothing of `Common` |
| `Framework.Observability.Tests` | The tests of the correlation identifier | `Framework.Observability` |
| `Framework.FeatureFlags`, with `--FeatureFlags` | The platform feature flags: the shared `features.json` catalogue, the Microsoft.FeatureManagement wiring, `GET /api/v1/features`, the Swagger filters for `[FeatureGate]`. A service with the flag references it | nothing of `Common` |
| `Framework.FeatureFlags.Tests` | The tests of feature flags | `Framework.FeatureFlags` |
| `Capabilities.Messaging.Notifications` | Email of `--Notify email`: the port, the SMTP and Graph transports, the sandbox. A service with the flag references it, so only that service carries MailKit and the Graph SDK | nothing of `Common` |
| `Capabilities.Messaging.Notifications.Tests` | The tests of email | `Capabilities.Messaging.Notifications`, `Common.Testing` |
| `Capabilities.Persistence.Mongo` | MongoDB of `--MongoDB`: the store, its options, its health check. A service with the flag references it, so only that service carries the driver | `Common`, `Common.Contracts` |
| `Capabilities.Persistence.Mongo.Tests` | The tests of MongoDB | `Capabilities.Persistence.Mongo`, `Common`, `Common.Testing` |
| `Capabilities.Analytics.ClickHouse` | ClickHouse of `--ClickHouse`: connections, the schema guard, the readiness check. A service with the flag references it, so only that service carries the driver | `Common`, `Common.Contracts` |
| `Capabilities.Analytics.ClickHouse.Tests` | The tests of ClickHouse | `Capabilities.Analytics.ClickHouse`, `Common`, `Common.Testing` |
| `Capabilities.Persistence.Abstractions` | The persistence contracts the kernel and the providers share: `IMigrationLock`, `IShortIdGenerator`, `IOutboxStatsDialect`, `MigrationLockKey` | nothing |
| `Capabilities.Persistence.S3` | Object storage of `--Storage`: `IS3ObjectStorage`, its settings, the null double. A service with the flag references it, so only that service carries the AWS SDK | `Common` |
| `Capabilities.Persistence.S3.Tests` | The tests of object storage | `Capabilities.Persistence.S3`, `Common`, `Common.Testing` |
| `Capabilities.Messaging.RabbitMQ` | The message bus of `--Messaging rabbitmq:outbox` or `direct`: the MassTransit transport on RabbitMQ, the transactional outbox and its diagnostics endpoint, the consumers. A service with the flag references it, so only that service carries the broker packages | `Common`, `Common.Application`, `Common.Contracts`, `Common.Infrastructure`, `Capabilities.Persistence.Abstractions`, `Framework.Observability` |
| `Capabilities.Messaging.RabbitMQ.Tests` | The tests of the bus: actor and trace propagation, endpoint names, outbox diagnostics | `Capabilities.Messaging.RabbitMQ`, `Common`, `Common.Infrastructure`, `Framework.Testing` |
| `Capabilities.Background.Hangfire` | Background jobs of `--Hangfire`: the storage in the service's schema, the dashboard, the filters that carry the actor and the domain-event scope into a job | `Common`, `Common.Infrastructure`, `Framework.DomainEvents`, `Framework.Observability` |
| `Capabilities.Background.Hangfire.Tests` | The tests of background jobs: the job's domain-event scope and the actor it carries | `Capabilities.Background.Hangfire`, `Common`, `Framework.Testing` |

## The shape of a module

A module of either family has the same skeleton: its own directory, its projects inside it, and
the project named after the module. A contract that crosses the module's boundary takes
`.Abstractions`; the tests take `.Tests` when the module has something to test; a module that owns
a mechanism carries a `README.md`. The package versions the module needs are declared in
`src/package-versions/<module>.props` and imported by `src/Directory.Packages.props`, so the flag
that turns the module on brings them with it. The wiring - `template.json`, `All.sln` and the docs
- names the module the same way. A difference from the skeleton is allowed, but it is recorded
with its reason beside the module.

## A service

| Project | Holds | References |
|---|---|---|
| `<Service>` | The service's domain: entities, aggregates, domain events, policies | `Common` |
| `<Service>.Application` | Use cases, domain event handlers, ports the infrastructure implements | `Common`, `Common.Application`, `Common.Contracts`, the domain |
| `<Service>.Infrastructure` | The `DbContext`, entity configurations, repositories, jobs, consumers | the domain, `Common`, `Common.Application`, `Common.Contracts`, `Common.Infrastructure`, the application |
| `<Service>.API` | Controllers, `Program`, the host setup | the `Common` projects, the application, the infrastructure |
| `<Service>.Tests` | The service's tests | `Framework.Testing`, `Common.Testing`, the `Common` projects, the service's projects |

The API project does not reference the domain: it works with the domain through the application layer,
which registers the domain services too. The application layer answers with the request and response
types of `Common.Contracts` and throws the exceptions of `Common`, so it references both; the
infrastructure references `Common.Contracts` for the bus messages its consumers receive. The infrastructure references the domain directly, because EF Core
maps the domain's entities and there are no separate persistence models to translate.

## Every reference is explicit

`Directory.Build.props` sets `DisableTransitiveProjectReferences`. A project sees only the projects it
references itself, not the projects those reference. Without it, the API project could use a domain type
through the application project, and the layering above would hold only as long as nobody tried. With it,
crossing a layer is a compile error, and adding a dependency is a visible line in a `.csproj`.

Packages still flow transitively; only project references are cut.

## Packages and versions

Package versions are declared once, in `src/Directory.Packages.props` or in a file it imports; a `.csproj`
names a package without a version. Every package stays on the .NET 8 line: none pulls in a .NET 9 library, directly or
transitively, so some packages are held below their newest version.

The versions a flag brings - the database, `--Notify`, `--MongoDB` and the rest - live in a file of their
own, `src/package-versions/<flag>.props`, which `Directory.Packages.props` imports by mask: a flag adds a
file and leaves the shared one as it is.
