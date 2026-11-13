# Persistence (PostgreSQL)

`Capabilities.Persistence.Postgres` owns what the kernel's persistence core delegates to the
database provider: the migration lock, the schema guard, the short-id generator, the
case-insensitive search and the outbox statistics dialect, all in PostgreSQL terms.

### What is inside

- `PostgresMigrationLock` - one migration of a schema at a time, whatever service runs it.
- `PostgresSchemaGuard` - the schema belongs to the service that names it, and the service refuses
  to start on a schema another service owns.
- `PostgresShortIdGenerator<TContext>` - the short id an entity gets from the database.
- `PostgresCaseInsensitiveSearch` - the `ILIKE` search the specifications ask for.
- `PostgresOutboxStatsDialect` - the outbox statistics in the provider's SQL.
- `PostgresErrors` - the provider's error codes, so the kernel does not read them itself.

What is deliberately not here: the `DbContext` options and the migration runner. They still live
in `NamespaceRoot.ProductName.Common.Infrastructure` and switch on the provider there - a known
debt of ADR-009 (`docs/adr/009`, issue #228), not the design of this module.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
