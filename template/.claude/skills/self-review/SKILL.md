---
name: self-review
description: Review your own staged or last commits against this solution's rules before a push - from the diff only.
---

Review staged or recent changes against the solution's rules.

## Usage
`/self-review` - reviews `git diff HEAD~1 HEAD` after a commit, or `git diff --staged` before one

## What to do

1. Run `git diff HEAD~1 HEAD` (or `git diff --staged` if nothing is committed yet).
2. Check each changed file against the list below.
3. Report each violation as `file:line - rule - one-line fix`.
4. If clean, say so in one line.

**Work from the diff only.** Do not read whole files for context; if the diff is not enough to judge a rule,
skip that rule.

## Checklist

### Money
- [ ] No `decimal`, `float`, `double` in money logic: `long` in minor units.
- [ ] Money columns are `bigint`, not `numeric` or `decimal`.

### Controllers (`/add-controller`)
- [ ] Reads return `Task<T>` directly - no `async`/`await`, no `IActionResult`; a create returns
      `ActionResult<T>` through `CreatedAtAction` (201).
- [ ] No `try`/`catch` in controllers.
- [ ] `[RequiredPermissions(...)]` on every action.
- [ ] An XML `<summary>` on every public member.
- [ ] Routes from constants in `Common.Contracts`.

### Access - three tiers
- [ ] Controller: `[RequiredPermissions]`.
- [ ] Service: the policy's `EnsureCan...`.
- [ ] Entity: invariants in the constructor and methods.

### Consumers
- [ ] A consumer inherits `BusCommandConsumer<T>` or `BusEventConsumer<T>`, never `IConsumer<T>` directly.
- [ ] A command consumer is named `{CommandTypeName}Consumer`.
- [ ] The consumer is idempotent on redelivery.

### Transactions (`/add-service-class`)
- [ ] **Every change** is inside `BeginTransactionAsync` / `CommitTransactionAsync` /
      `RollbackTransactionAsync`, even a single-entity create.
- [ ] The `catch` logs with the entity type and id.
- [ ] No `SaveChangesAsync` before `CommitTransactionAsync`.
- [ ] A create a client may retry goes through `IIdempotentExecutor`.

### Domain events (`/add-domain-event`)
- [ ] One class, one phase.
- [ ] Raised on the aggregate after the change; no `IDomainEventDispatcher` in service code.

### Contracts
- [ ] A list response implements `IItemsResponse<T>` or `IPaginatedResponse<T>`, never a bare array.
- [ ] `Items` has a `[JsonPropertyName("semantic_name")]`.
- [ ] Filter records are `sealed record` with `{ get; init; }`.
- [ ] No response property named like a token.

### Configuration
- [ ] No `IConfiguration["Key"]` in a service: typed options.
- [ ] No secret, key or connection string in a committed file.

### Tests (`/add-tests`)
- [ ] New or changed service or handler logic has tests in the same commit - **missing tests block the
      push**:
  - a service with business logic (not plain CRUD) - a test class;
  - a domain event handler - at least one test;
  - a complex domain method (a state machine, a calculation, several steps) - a test class.
- [ ] `[Test(Description = "...")]` on every test.
- [ ] Shouldly only, no `Assert.*`.
- [ ] Mock matching by value, never by reference.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
