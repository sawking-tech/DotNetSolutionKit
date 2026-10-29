#!/usr/bin/env bash
# The S3-compatible storage the integration tests run on, with the bucket "tests"; ready once it exists.
#   bash tests/servers/s3.sh start | ready | logs | down            (or all of them: bash tests/servers/up.sh)
set -euo pipefail
# Git Bash on Windows rewrites container paths such as /data into Windows paths; this keeps them as they are.
export MSYS_NO_PATHCONV=1
case "${1:-}" in
    start)
        docker rm -f tests-s3 >/dev/null 2>&1 || true
        docker run -d --name tests-s3 -p 18333:8333 -e AWS_ACCESS_KEY_ID=tester -e AWS_SECRET_ACCESS_KEY=test-do-not-use \
            chrislusf/seaweedfs server -s3 -dir=/data >/dev/null
        echo "TEST_S3=ServiceUrl=http://localhost:18333;Bucket=tests;AccessKey=tester;SecretKey=test-do-not-use" ;;
    ready) echo "s3.bucket.create -name tests" | docker exec -i tests-s3 weed shell -master=localhost:9333 2>&1 | grep -qE 'created|exists' ;;
    logs) docker logs tests-s3 ;;
    down) docker rm -f tests-s3 >/dev/null 2>&1 || true ;;
    *) echo "usage: $0 start|ready|logs|down" >&2; exit 2 ;;
esac
