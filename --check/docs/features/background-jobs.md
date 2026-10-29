# Background jobs

On by default; `--Hangfire false` generates a service without them. Jobs run on Hangfire, stored in
the solution's database, PostgreSQL or SQL Server, in a schema of the service's own (`orders_hangfire`),
next to the service's schema and guarded the same way.

## Configuration

```json
"HangfireSettings": {
  "DashboardUser": "admin",
  "DashboardPassword": "",
  "WorkerCount": 10
}
```

The password is required and has no default: the service does not start without it. The dashboard is at
`/hangfire`, behind basic authentication with these credentials.

## Write and enqueue a job

A job is a class in the service's infrastructure; it is resolved from the container in a scope of its own:

```csharp
public sealed class ExpireUnpaidOrdersJob(IOrderRepository orders, IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync()
    {
        // ...
        await unitOfWork.SaveChangesAsync();
    }
}

RecurringJob.AddOrUpdate<ExpireUnpaidOrdersJob>("expire-unpaid-orders", job => job.ExecuteAsync(), Cron.Hourly());
BackgroundJob.Enqueue<ExpireUnpaidOrdersJob>(job => job.ExecuteAsync());
```

What the template adds to plain Hangfire:

- Domain events work in jobs. `DomainEventJobActivator` gives each job a scope the domain event
  interceptors can see, so events raised by a job are dispatched as in a request.
- The actor travels with the job. `JobActorPropagationFilter` records who enqueued a job and restores
  that user while the job runs, so what the job writes is attributed to them. A scheduled run has no such
  user and runs as the system. A job that acts on its own account even when someone enqueued it takes
  its context from `ISystemExecutionContextFactory.Create()`.
- The correlation travels with every job. The same filter records the trace and the
  [correlation identifier](../architecture/web-layer.md#correlation) of the work that enqueued the job,
  so its log lines are found with the request's. A scheduled run starts a trace of its own and is
  correlated by it.
- `/ready` checks Hangfire: the health check fails when no Hangfire server is running.

## Why Hangfire

Of the job libraries for .NET it is the most convenient for a service's tasks:

- fire-and-forget, delayed and recurring jobs through one compact API, with retries and the history of
  each run stored in the solution's database;
- a dashboard out of the box: what ran, what failed and why, with a button to retry;
- jobs are plain classes resolved from DI, so they are tested like any other class.

Quartz.NET is much harder to maintain: the same job takes more code, and a dashboard is not part of it.

The template has no other way to run jobs. Where Hangfire does not fit, generate the service with
`--Hangfire false` and run background jobs however suits you (an external cron or a Kubernetes
CronJob, a hosted service, delayed messages on the bus).

## Tests

A job only orchestrates; its tests mock the ports it calls and verify the calls, and the logic it calls is
tested as a service. See [ADR-005](../adr/005-testing-a-service.md). `Common.Tests` covers the job
activator.
