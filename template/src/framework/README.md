# Framework

The systems of work the template brings, always on and used as they are — the candidates for a
package of their own. A namespace names its kind: `NamespaceRoot.ProductName.Framework.*`, the way
`NamespaceRoot.ProductName.Capabilities.*` names a capability.

- `NamespaceRoot.ProductName.Framework.DomainEvents` — the detachable seam of the three-phase
  domain events (ADR-001): the handler interfaces of the phases, the dispatcher and the event
  storage interface. The EF Core pipeline that dispatches the phases (the interceptors in
  `Common.Infrastructure/Persistence/EntityFramework/Events`) and the domain event primitives
  (`IDomainEvent`, `IHasDomainEvents`, `EventfulEntity`, `AggregateRoot` in `Common`) stay where
  they are entangled with the `DbContext` pipeline and the core execution context; see the project
  Readme.
- `NamespaceRoot.ProductName.Framework.Auth` — authentication and authorization: the JWT bearer and
  the API key, the token cookies with their CSRF contract, and the permission check a controller
  declares.
- `NamespaceRoot.ProductName.Framework.Observability` — the correlation identifier: the ambient
  value carried across a request, a job and a bus message, the header that carries it between
  services, and the middleware that reads or creates it per request. The logging configuration
  stays with the team in Common; see the project Readme.
- `NamespaceRoot.ProductName.Framework.OpenApi` — the Swagger stack: the versioned documents, the
  filters, the gateway page and the schema dump the contract check reads.
- `NamespaceRoot.ProductName.Framework.Validation` — the FluentValidation wiring: the validators of
  the service's assembly run before a handler, and a bad request answers in the common shape.
- `NamespaceRoot.ProductName.Framework.FeatureFlags`, with `--FeatureFlags` — the platform feature
  flags: the shared `features.json` catalogue and the Microsoft.FeatureManagement wiring.
- `NamespaceRoot.ProductName.Framework.Testing` — the portable testing harness (ADR-005): the
  execution contexts, the domain event test wiring, stubs and code rules. Helpers tied to an
  external system (the PostgreSQL and SQL Server test databases) and the audit verifiers stay in
  `NamespaceRoot.ProductName.Common.Testing`; see the project Readme.

Every module has the same skeleton: its directory, the project named after it, `.Tests` when there
is something to test, a `README.md` when it owns a mechanism, and the versions it needs in
`src/package-versions/<module>.props`. Apart from `FeatureFlags`, which a service takes with
`--FeatureFlags`, nothing here is behind a flag: every generated service references these
projects, as it references the Common projects.
