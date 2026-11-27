#!/usr/bin/env bash
#
# Waits for the checks of a commit to settle and fails unless every one of them passed:
#
#   scripts/wait-for-checks.sh <repository> <commit> [<minutes>]
#
# A check settles green as success, skipped or neutral: skipped is a check that does not apply to this
# commit, as the site's publish outside master. Anything else once settled - a failure, a cancellation,
# a timeout - fails the wait, and so does a check still running when the minutes are gone.
#
# A commit with no checks at all fails too: no run is not a pass, and a fast-forward that leaned on an
# empty list would take any commit at all.
#
# A push made with GITHUB_TOKEN does not go through the branch protection, so this script is the barrier
# a fast-forward leans on, not a second opinion on one GitHub already applied.
set -euo pipefail

repository="$1" commit="$2" minutes="${3:-40}"

fail() { echo "::error::$1"; exit 1; }

# One line per check: "<name> <status> <conclusion>"; a check not started has no conclusion yet.
checks() {
    gh api "repos/$repository/commits/$commit/check-runs?per_page=100" --paginate 2>/dev/null \
        | jq -r -s '.[].check_runs[] | "\(.name)\t\(.status)\t\(.conclusion // "-")"'
}

# The checks that are not settled green: running, queued, or settled as anything but a pass.
unsettled() {
    awk -F'\t' '{
        if ($2 != "completed")              printf "  %s: %s\n", $1, $2
        else if ($3 != "success" && $3 != "skipped" && $3 != "neutral")
                                            printf "  %s: %s\n", $1, $3
    }'
}

for minute in $(seq 1 "$minutes"); do
    output=$(checks)
    total=$(grep -c . <<< "$output" || true)
    [ "$total" -gt 0 ] || fail "no checks ran on ${commit:0:7}: nothing establishes that it builds"

    left=$(unsettled <<< "$output")
    if [ -z "$left" ]; then
        echo "every check of ${commit:0:7} passed: $total on the commit"
        exit 0
    fi

    if grep -qE ': (failure|cancelled|timed_out|action_required|startup_failure)$' <<< "$left"; then
        echo "::error::checks did not pass on ${commit:0:7}:"
        echo "$left"
        exit 1
    fi

    [ "$minute" -lt "$minutes" ] || { echo "::error::checks did not settle on ${commit:0:7} after $minutes minute(s):"; echo "$left"; exit 1; }
    sleep 60
done
