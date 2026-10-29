#!/usr/bin/env bash
# The Vault the integration tests run on, in dev mode with a known root token.
#   bash tests/servers/vault.sh start | ready | logs | down         (or all of them: bash tests/servers/up.sh)
set -euo pipefail
# Git Bash on Windows rewrites container paths such as /data into Windows paths; this keeps them as they are.
export MSYS_NO_PATHCONV=1
case "${1:-}" in
    start)
        docker rm -f tests-vault >/dev/null 2>&1 || true
        docker run -d --name tests-vault -p 18200:8200 -e VAULT_DEV_ROOT_TOKEN_ID=test-do-not-use \
            -e VAULT_DEV_LISTEN_ADDRESS=0.0.0.0:8200 hashicorp/vault:1.17 >/dev/null
        echo "TEST_VAULT=Address=http://localhost:18200;Token=test-do-not-use" ;;
    ready) curl -fsS http://localhost:18200/v1/sys/health >/dev/null 2>&1 ;;
    logs) docker logs tests-vault ;;
    down) docker rm -f tests-vault >/dev/null 2>&1 || true ;;
    *) echo "usage: $0 start|ready|logs|down" >&2; exit 2 ;;
esac
