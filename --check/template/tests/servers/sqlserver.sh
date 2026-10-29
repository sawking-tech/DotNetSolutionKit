#!/usr/bin/env bash
# The SQL Server the integration tests run on. SQL Server on Linux does not run its files on a ramdisk.
#   bash tests/servers/sqlserver.sh start | ready | logs | down     (or all of them: bash tests/servers/up.sh)
set -euo pipefail
# Git Bash on Windows rewrites container paths such as /opt/... into Windows paths; this keeps them as they are.
export MSYS_NO_PATHCONV=1
case "${1:-}" in
    start)
        docker rm -f tests-mssql >/dev/null 2>&1 || true
        docker run -d --name tests-mssql -p 11433:1433 -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Test-do-not-use-1 \
            mcr.microsoft.com/mssql/server:2022-latest >/dev/null
        echo "TEST_SQLSERVER=Server=localhost,11433;User Id=sa;Password=Test-do-not-use-1;TrustServerCertificate=true" ;;
    ready) docker exec tests-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P Test-do-not-use-1 -Q "SELECT 1" -b >/dev/null 2>&1 ;;
    logs) docker logs tests-mssql ;;
    down) docker rm -f tests-mssql >/dev/null 2>&1 || true ;;
    *) echo "usage: $0 start|ready|logs|down" >&2; exit 2 ;;
esac
