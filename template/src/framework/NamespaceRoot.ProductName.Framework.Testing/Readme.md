# Framework.Testing

The portable part of the testing system (ADR-005): the test execution contexts, the domain event
test wiring, stubs for the ports that leave a service, and the code rules.

- `TestExecutionContext` and its ladder (`ServiceTestExecutionContext`, `DbTestExecutionContext`,
  `ServiceDbTestExecutionContext`, `InMemoryTestExecutionContext`): a real DI container per test, a
  database per test, arrange/act/assert in separate scopes, mocks that survive scopes. `ActAsync`
  of the service-and-database contexts publishes its scope to the domain event interceptors, so the
  pre-save and post-commit handlers run as in the service.
- `DomainEventsTestExtensions.AddDomainEvents`: registers the dispatcher and an explicit handler
  set for a test. It wires the EF pipeline from `Common.Infrastructure`
  (`AddDomainEventCore` / `AddDomainEventPersistence`), so this project references it; the pipeline
  itself is not portable and stays there.
- `Stubs`: `UserContextMock`, `TimeProviderMock`, `RecordingMessageBus`, `NoOpAuditRecorder`, the
  in-memory case-insensitive searches and `LikePattern`.
- `Rules`: `TokensInResponses`, a rule check over controllers.
- `TestSkip`, `TestHelpers`, `TestDomainExecutionContext`: the small helpers every test project
  sets up or reaches for.

What stays in `NamespaceRoot.ProductName.Common.Testing`, because it is tied to an external system
or to the audit pipeline rather than to the way of testing:

- `PostgresDbTestExecutionContext` / `SqlServerDbTestExecutionContext` with `Integration/Postgres`
  and `Integration/SqlServer` — the real-database test servers.
- The audit verifiers (`AuditMarkerVerifier`, `AuditRedactionVerifier`, `SetBasedWriteVerifier`)
  and `TestCategories` alongside them.

No test framework here: a test project adds its own and tells `TestSkip` how it skips a test, from
its `TestFramework.cs`.
