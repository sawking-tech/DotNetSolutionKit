---
name: ef-migration
description: Add an EF Core migration to a service of this solution, with the project paths the design-time factory needs.
---

Add an EF Core migration to a service.

## Usage
`/ef-migration <ServiceName> <MigrationName>` - e.g. `/ef-migration Billing AddInvoiceNumberIndex`

## Paths

For a service `<ServiceName>`, both the project and the startup project are its Infrastructure project:

```
src/services/NamespaceRoot.ProductName.<ServiceName>/NamespaceRoot.ProductName.<ServiceName>.Infrastructure
```

**Why Infrastructure is the startup project:** each service implements
`IDesignTimeDbContextFactory<TDbContext>` in Infrastructure (`EntityFramework/*DbContextFactory.cs`).
The EF tools build the `DbContext` from it at design time, with no API host and no database, so the API
project is not needed and its startup code does not run.

## Command (from the service folder)

```bash
cd src/services/NamespaceRoot.ProductName.<ServiceName>
dotnet ef migrations add <MigrationName> \
  -p NamespaceRoot.ProductName.<ServiceName>.Infrastructure \
  -s NamespaceRoot.ProductName.<ServiceName>.Infrastructure \
  -o EntityFramework/Migrations
```

`dotnet ef` needs the `dotnet-ef` tool of the solution's EF Core major version and a restored project: run
`dotnet restore` first if the build fails with `NETSDK1004`.

## What to do

1. Map the service name to the paths above.
2. Run the command.
3. Show the generated migration file path.
4. Read `Up` and `Down` before committing, above all a `DropColumn`, `DropTable` or `AlterColumn` on a column
   with data: EF writes what the model says, not what keeps the data.

## Notes

- The migrations run at startup, under a lock, so two replicas do not migrate one schema twice:
  [persistence](https://dotskit.sawking.tech/docs.html#persistence).
- With `--Messaging outbox` the first migration also creates the outbox tables.
- A table that needs SQL the model cannot express (a sequence for readable numbers, a partial index) is
  written in the migration with `migrationBuilder.Sql(...)`.
- Check the SQL before a release: `dotnet ef migrations script --idempotent -p ... -s ...`.
- Money columns hold integers in minor units, `bigint`; never `numeric` or a floating type (see
  `/add-service-class`, money).

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
