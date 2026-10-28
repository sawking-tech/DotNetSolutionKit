---
name: pick-pattern
description: Pick the design pattern before writing service-layer code with non-trivial structure - a state machine, a plug-in point, a cascade across services, deduplication, rate limiting, a projection.
---

# pick-pattern

Decide which named pattern fits before writing the code. If the answer is "none", say so and write the
code plainly: a pattern applied too early is worse than none.

## Usage

`/pick-pattern <feature or method description>` - e.g. `/pick-pattern cancel an order, refund it and notify the warehouse`.

Answer: the pattern or patterns, where they go in this solution, and one alternative considered and
rejected.

## Compose, do not inherit - the default

Before the catalogue: **compose, do not inherit**. Two classes that share behaviour get a reference to a
third that holds it, or both call a service that does it; they do not get a base class. A service that
wraps and delegates is almost always the right shape.

- Replacing a strategy at runtime is a DI registration with composition; with inheritance it rebuilds
  the type graph.
- Composed dependencies are explicit and narrow, and a test replaces them; inherited behaviour is implicit
  and leaks into every override.
- Inheritance ties constructor chains and virtual dispatch to the business problem; they are different
  axes.

Pick inheritance (a template method on a base class) only when **every** subtype shares one hot path with
a small, fixed set of variation points. Otherwise compose.

## Catalogue

| Pattern | When | Where in this solution |
|---|---|---|
| **Specification** | Composable rules and queries tested on their own | `Domain/<Area>/Specifications`, `/add-specification` |
| **Strategy** | Interchangeable algorithms behind one interface, chosen by configuration | an interface in the application layer, implementations registered by an option |
| **Chain of Responsibility** | Evaluators with one input and output, run in order, the first match wins | an ordered `IEnumerable<IEvaluator>` from DI |
| **Adapter / Anti-corruption layer** | An external system whose model must not leak in | a port in the application layer, the adapter in infrastructure |
| **Process Manager (light) / Saga (heavy)** | A long flow across services with a correlation id that converges or fails | a state marker on the aggregate, events over the bus, a watchdog job |
| **Event-Carried State Transfer** | A service needs a copy of another's state and must not query back | a consumer that keeps a projection table in its own schema |
| **Transactional Outbox** | A message must go out exactly when the change commits | `--Messaging outbox`, a pre-save handler: `/add-domain-event` |
| **Idempotent Receiver** | Redelivery from the bus, a job retry or a webhook must not repeat the effect | `IIdempotentExecutor`, a correlation log, a state marker |
| **Rate Limiter** | Protect a downstream with a documented capacity | `System.Threading.RateLimiting`; a `DelegatingHandler` on the HTTP client |
| **Circuit Breaker** | Fail fast while a downstream is degraded | a resilience handler on the HTTP client (`Microsoft.Extensions.Http.Resilience`); the template wires none |
| **Retry with backoff** | Transient failures that clear on their own | a resilience handler on the HTTP client; the bus retries itself (`RabbitMq:RetryLimit`) |
| **Watchdog / Reaper** | Recover entities stuck in an in-flight state | a recurring Hangfire job (`-H`) |
| **Batched producer** | Many small actions become one bulk call | a job that collects intents and sends them per interval |
| **Diff / Delta transfer** | The downstream already knows the previous state | a hash of what was sent, compared before the call |
| **CQRS** | The read model differs from the write model in shape, scale or audience | a projection, ClickHouse with `--ClickHouse` |
| **Optimistic concurrency** | Two writers may race and "the first commit wins" is acceptable | a concurrency token: `xmin` on PostgreSQL, `rowversion` on SQL Server |
| **Compare-and-set** | Set a marker only if it is not set yet, without a row lock | a conditional `ExecuteUpdateAsync` on the marker |
| **Two-tier toggle** | An operator switch that applies at once, plus a kill switch | a feature flag (`--FeatureFlags`) over a configuration switch |
| **Audit log** | Who changed what and when, without snapshots | the audit journal (`--Audit`) |

## Decision flow

1. **A boolean rule or a query?** Specification.
2. **An algorithm an operator chooses?** Strategy.
3. **A pipeline of evaluators?** Chain of Responsibility.
4. **An external system?** Adapter, and an anti-corruption layer if its model differs sharply.
5. **A flow of several steps across services?** Process Manager + Outbox + Idempotent Receiver.
6. **Protecting a downstream?** Rate Limiter + Circuit Breaker + Retry with backoff.
7. **Redelivery or replay?** Idempotent Receiver - never just "check if it exists".
8. **Bulk or per item?** Batched producer, plus Diff transfer when the scale needs it.
9. **An optional feature?** A two-tier toggle, never a single boolean.
10. **A history?** The audit log; no "current state" column without a log unless the column itself is
    the source of truth.

If none fits cleanly, the change is straightforward: skip the ceremony. Three duplicated lines beat a
premature abstraction.

## Answer

```
Pattern: <one, or a layered combination>
Reason: <one sentence on why it fits>
Where: <file path or layer>
Rejected: <pattern>, because <reason>
```

Under six lines. If more is needed, the problem is bigger than one pattern, and the next step is a design
note, not more lines here.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
