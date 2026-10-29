#!/usr/bin/env bash
#
# docker compose over the infrastructure and every service of the solution:
#
#   deploy/compose/compose.sh up -d --wait     start, and wait until every service is ready
#   deploy/compose/compose.sh logs -f
#   deploy/compose/compose.sh down
#
# Each part of the infrastructure is a file in deploy/compose/infra/, and each service brings its own
# src/services/<Service>/deploy/compose.yml, so a flag or a service added later joins with a file of its own
# and edits no shared one. Paths in those files are relative to the solution root.
set -euo pipefail

cd "$(dirname "$0")/../.."

files=()
for part in deploy/compose/infra/*.yml; do
    [ -e "$part" ] && files+=(-f "$part")
done
for service in src/services/*/deploy/compose.yml; do
    [ -e "$service" ] && files+=(-f "$service")
done

env_file=()
[ -f deploy/compose/.env ] && env_file=(--env-file deploy/compose/.env)

exec docker compose --project-directory . "${env_file[@]}" "${files[@]}" "$@"
