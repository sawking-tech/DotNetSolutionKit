#!/usr/bin/env bash
# The PostgreSQL the integration tests run on, on a ramdisk with durability off: several times faster,
# safe only because the data dies with the container. The ramdisk is larger than max_wal_size.
#   bash tests/servers/postgres.sh start | ready | logs | down      (or all of them: bash tests/servers/up.sh)
set -euo pipefail
# Git Bash on Windows rewrites container paths such as /data into Windows paths; this keeps them as they are.
export MSYS_NO_PATHCONV=1
case "${1:-}" in
    start)
        docker rm -f tests-pg >/dev/null 2>&1 || true
        docker run -d --name tests-pg -p 15433:5432 -e POSTGRES_PASSWORD=test-do-not-use \
            --tmpfs /var/lib/postgresql/data:rw,size=2g postgres:16-alpine \
            -c fsync=off -c synchronous_commit=off -c full_page_writes=off -c max_wal_size=512MB -c autovacuum=off >/dev/null
        echo "TEST_POSTGRES=Host=localhost;Port=15433;Database=postgres;Username=postgres;Password=test-do-not-use" ;;
    ready) docker exec tests-pg pg_isready -U postgres >/dev/null 2>&1 ;;
    logs) docker logs tests-pg ;;
    down) docker rm -f tests-pg >/dev/null 2>&1 || true ;;
    *) echo "usage: $0 start|ready|logs|down" >&2; exit 2 ;;
esac
