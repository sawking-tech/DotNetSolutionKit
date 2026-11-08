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
- `NamespaceRoot.ProductName.Framework.Testing` — the portable testing harness (ADR-005): the
  execution contexts, the domain event test wiring, stubs and code rules. Helpers tied to an
  external system (the PostgreSQL and SQL Server test databases) and the audit verifiers stay in
  `NamespaceRoot.ProductName.Common.Testing`; see the project Readme.

Unlike `src/capabilities`, nothing here is behind a flag: every generated service references both
projects, as it references the Common projects.
