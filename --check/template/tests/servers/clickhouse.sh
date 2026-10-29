#!/usr/bin/env bash
# The ClickHouse the integration tests run on.
#   bash tests/servers/clickhouse.sh start | ready | logs | down    (or all of them: bash tests/servers/up.sh)
set -euo pipefail
# Git Bash on Windows rewrites container paths such as /data into Windows paths; this keeps them as they are.
export MSYS_NO_PATHCONV=1
case "${1:-}" in
    start)
        docker rm -f tests-ch >/dev/null 2>&1 || true
        docker run -d --name tests-ch -p 18123:8123 -e CLICKHOUSE_USER=tester -e CLICKHOUSE_PASSWORD=test-do-not-use \
            -e CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT=1 clickhouse/clickhouse-server:24.8-alpine >/dev/null
        echo "TEST_CLICKHOUSE=Host=localhost;Port=18123;Username=tester;Password=test-do-not-use" ;;
    ready) curl -fsS http://localhost:18123/ping >/dev/null 2>&1 ;;
    logs) docker logs tests-ch ;;
    down) docker rm -f tests-ch >/dev/null 2>&1 || true ;;
    *) echo "usage: $0 start|ready|logs|down" >&2; exit 2 ;;
esac
