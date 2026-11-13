# Background Jobs (Hangfire)

`Capabilities.Background.Hangfire` gives a service its background jobs and the dashboard that
shows them. The jobs live in the service's own schema of the solution's database; the module owns
the Hangfire wiring, the filters a job runs with, and the activator that lets a job raise domain
events.

`AddHangfireJobs` takes the connection string, the service's name and the schema the job tables
live in:

```csharp
services.AddHangfireJobs(connectionString, "Orders", "orders_jobs");
```

### What is inside

- `DependencyInjection.AddHangfireJobs` - Hangfire, its storage for the database the solution
  uses (PostgreSQL or SQL Server), the dashboard and the job filters.
- `HangfireDashboard` / `HangfireAdminAuthorizationFilter` - the dashboard is for the
  administrator, not for every caller.
- `DomainEventJobActivator` - a job runs in its own scope, so the events it raises are dispatched
  and its PostCommit handlers run.
- `JobActorPropagationFilter` / `HangfireFilterInstaller` - the actor of the request that scheduled
  the job reaches the job.
- `HangfireSettings` - the switches read from configuration.

What is deliberately not here: the jobs themselves - they belong to the service that schedules
them; the choice of the database - Hangfire keeps a storage package per database, so the wiring
knows the provider, and the provider switch itself still lives in
`NamespaceRoot.ProductName.Common.Infrastructure` (a known debt, `docs/adr/009`, issue #228).

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
