# Startup checks

A misconfigured service stops at startup, with the reason in its log, rather than failing on the first
request that needs the missing piece.

| Check | Fails when |
|---|---|
| Options validated on start | a required setting is empty or out of range: the internal API key, the Hangfire dashboard password, the CORS preflight age |
| Connection string | `ConnectionStrings:DefaultConnection` is missing |
| Schema guard | the service's schema belongs to another service; see [persistence](../architecture/persistence.md) |
| DI validation | a registration is missing, or a scoped service is taken from the root; done when the container is built, in every environment |
| Secret store, with `-I` | the store is unreachable outside `Local` |

## A failed start is loud

A service that fails to start logs the exception as fatal, writes it to standard error as well (the
failure may come before logging is configured), and exits with code 1. An orchestrator sees a failed
start instead of a process that exited cleanly.

## Waiting for the database

A service started together with its database does not fail while the database is coming up: the schema
guard retries for about 30 seconds while PostgreSQL is starting or its port is still closed. Replicas
starting together take turns: the guard holds a lock per schema while it creates and claims it.
