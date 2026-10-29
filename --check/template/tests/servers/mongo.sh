#!/usr/bin/env bash
# The MongoDB the integration tests run on.
#   bash tests/servers/mongo.sh start | ready | logs | down         (or all of them: bash tests/servers/up.sh)
set -euo pipefail
# Git Bash on Windows rewrites container paths such as /data into Windows paths; this keeps them as they are.
export MSYS_NO_PATHCONV=1
case "${1:-}" in
    start)
        docker rm -f tests-mongo >/dev/null 2>&1 || true
        docker run -d --name tests-mongo -p 17017:27017 \
            -e MONGO_INITDB_ROOT_USERNAME=tester -e MONGO_INITDB_ROOT_PASSWORD=test-do-not-use mongo:7.0 >/dev/null
        echo "TEST_MONGO=mongodb://tester:test-do-not-use@localhost:17017/?authSource=admin" ;;
    ready) docker exec tests-mongo mongosh --quiet -u tester -p test-do-not-use --eval "db.adminCommand('ping').ok" >/dev/null 2>&1 ;;
    logs) docker logs tests-mongo ;;
    down) docker rm -f tests-mongo >/dev/null 2>&1 || true ;;
    *) echo "usage: $0 start|ready|logs|down" >&2; exit 2 ;;
esac
