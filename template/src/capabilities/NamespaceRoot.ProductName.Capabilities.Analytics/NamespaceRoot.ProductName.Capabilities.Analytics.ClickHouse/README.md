# Capabilities.Analytics.ClickHouse

ClickHouse of the flag `--ClickHouse`: connections, the schema guard, the readiness check and the
value helpers, registered from the `ClickHouse` section by `AddClickHouse` in a service that uses it.

A service references this project only with the flag, so only that service carries the ClickHouse
driver. Readers and writers of the data stay in the service, behind its own ports.
