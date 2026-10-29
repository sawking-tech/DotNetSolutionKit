#!/usr/bin/env bash
#
# Starts every test server of the solution - a script per part in this folder; a flag added later brings
# its own - waits until each is ready, and gives the integration tests their TEST_* variables:
#
#   eval "$(bash tests/servers/up.sh)"     locally: exports the variables into the shell
#   bash tests/servers/up.sh               in CI, with GITHUB_ENV set: appends them to $GITHUB_ENV
#   bash tests/servers/up.sh start         starts and gives the variables without waiting;
#   bash tests/servers/up.sh wait          waits for them - CI runs the unit tests in between
#   bash tests/servers/up.sh down          removes the containers
set -euo pipefail
# Git Bash on Windows rewrites container paths such as /data into Windows paths; this keeps them as they are.
export MSYS_NO_PATHCONV=1

here="$(cd "$(dirname "$0")" && pwd)"
parts=()
for script in "$here"/*.sh; do
    [ "$(basename "$script")" = up.sh ] || parts+=("$script")
done
mode="${1:-all}"
case "$mode" in all|start|wait|down) ;; *) echo "usage: $0 [start|wait|down]" >&2; exit 2 ;; esac

if [ "$mode" = down ]; then
    for script in "${parts[@]}"; do bash "$script" down; done
    exit 0
fi

if [ "$mode" != wait ]; then
    vars=""
    for script in "${parts[@]}"; do
        vars+="$(bash "$script" start)"$'\n'
    done
    # The values carry ';', so locally they are quoted for the shell; $GITHUB_ENV takes them raw.
    while IFS= read -r line; do
        [ -n "$line" ] || continue
        if [ -n "${GITHUB_ENV:-}" ]; then
            echo "$line" >> "$GITHUB_ENV"
        else
            printf "export %s='%s'\n" "${line%%=*}" "${line#*=}"
        fi
    done <<< "$vars"
fi
[ "$mode" = start ] && exit 0

ready=0
for attempt in $(seq 1 60); do
    ready=1
    for script in "${parts[@]}"; do
        bash "$script" ready || { ready=0; break; }
    done
    [ "$ready" = 1 ] && break
    sleep 1
done
if [ "$ready" != 1 ]; then
    for script in "${parts[@]}"; do
        bash "$script" ready || { echo "$(basename "$script" .sh) is not ready:" >&2; bash "$script" logs >&2; }
    done
    exit 1
fi
