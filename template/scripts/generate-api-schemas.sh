#!/usr/bin/env bash
# Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.
#
#
# Generates the API contract of every service.
#
# One file per service under <project>/api-schema/<service>.json. The files are not committed: CI
# generates them on both sides of a pull request and diffs them (.github/workflows/api-diff.yml).
#
# Each service writes its own document straight out of its built application (--dump-schema) and
# exits. Nothing is served: no port to bind, no readiness to wait for, no process to kill. Starting
# each service and fetching the document over HTTP could not tell a started service from a leftover
# one still holding the port, and every document came back identical to the first service's while
# the run reported success.
#
# Infrastructure stays out of the way through schema-only mode: no database, no broker, no jobs.
set -euo pipefail

cd "$(dirname "$0")/.."

export SCHEMA_ONLY=1
# Pinned so the document is byte-identical everywhere: the environment name ends up in its title.
export ASPNETCORE_ENVIRONMENT=Local

# `python3` on Windows resolves to the Store stub, which exits without running anything.
PYTHON="${PYTHON:-}"
if [ -z "$PYTHON" ]; then
    if command -v python3 >/dev/null 2>&1 && python3 -c "" >/dev/null 2>&1; then
        PYTHON=python3
    else
        PYTHON=python
    fi
fi

# Absolute, and in the form the runtime understands: the service is a Windows process under Git Bash,
# where a path like /c/... means nothing, and a relative --contentRoot is resolved against the
# assembly's own directory rather than this one.
REPO_ROOT=$(pwd -W 2>/dev/null || pwd)

# Every service exposes the "all" document: every version of its API in one file.
DOCUMENT=all

for csproj in src/services/*/*.API/*.API.csproj; do
    project=$(dirname "$csproj")
    assembly=$(basename "$csproj" .csproj)
    service=$(basename "$project" .API)
    # The full name, not its last segment: Sales.Orders and Purchasing.Orders are different services.
    key=$(echo "$service" | tr '[:upper:]' '[:lower:]')
    log=$(mktemp)
    echo "==> $key"
    # The gateway serves the services' documents and has none of its own to compare.
    if grep -q "<IsApiGateway>true</IsApiGateway>" "$csproj"; then
        echo "    a gateway: no contract of its own"
        continue
    fi

    if ! dotnet build "$csproj" --nologo -v q >"$log" 2>&1; then
        echo "build failed for $key:" >&2; cat "$log" >&2; exit 1
    fi

    mkdir -p "$project/api-schema"
    # Kept inside the project rather than in a temporary directory: the service is a Windows process
    # and would read `/tmp/...` from mktemp as a path on the current drive, while python reads the
    # one Git Bash means. The document would then be written to one file and parsed from another.
    raw="$REPO_ROOT/$project/obj/api-schema.raw.json"

    rm -f "$raw"

    if ! dotnet "$project/bin/Debug/net8.0/$assembly.dll" \
        --contentRoot "$REPO_ROOT/$project/bin/Debug/net8.0" \
        --dump-schema "$raw" --document "$DOCUMENT" >"$log" 2>&1; then
        echo "$key could not write its $DOCUMENT document:" >&2; cat "$log" >&2; exit 1
    fi

    # A startup failure is caught and logged inside the service, which then exits zero, so the exit
    # code alone would call a silent failure a success.
    if [ ! -s "$raw" ]; then
        echo "$key exited without writing $raw:" >&2; cat "$log" >&2; exit 1
    fi

    "$PYTHON" scripts/normalise_api_schema.py "$raw" "$project/api-schema/$key.json"
    rm -f "$raw" "$log"
done

echo
echo "Done. Contracts live in src/services/*/*.API/api-schema/<service>.json"
