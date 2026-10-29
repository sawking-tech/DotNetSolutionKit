#!/usr/bin/env bash
# The SMTP server the integration tests of email send to; its API lets a test read what arrived.
#   bash tests/servers/mailhog.sh start | ready | logs | down       (or all of them: bash tests/servers/up.sh)
set -euo pipefail
# Git Bash on Windows rewrites container paths such as /data into Windows paths; this keeps them as they are.
export MSYS_NO_PATHCONV=1
case "${1:-}" in
    start)
        docker rm -f tests-smtp >/dev/null 2>&1 || true
        docker run -d --name tests-smtp -p 11025:1025 -p 18025:8025 mailhog/mailhog:v1.0.1 >/dev/null
        echo "TEST_SMTP=localhost:11025"
        echo "TEST_SMTP_API=http://localhost:18025/" ;;
    ready) curl -fsS "http://localhost:18025/api/v2/messages?limit=1" >/dev/null 2>&1 ;;
    logs) docker logs tests-smtp ;;
    down) docker rm -f tests-smtp >/dev/null 2>&1 || true ;;
    *) echo "usage: $0 start|ready|logs|down" >&2; exit 2 ;;
esac
