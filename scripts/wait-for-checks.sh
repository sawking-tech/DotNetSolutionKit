#!/usr/bin/env bash
#
# Fails unless a fast-forward of <source> into <target> may proceed: every commit the move would bring is
# checked, not only the last one.
#
#   scripts/wait-for-checks.sh <repository> <target-branch> <source-branch> [<minutes>]
#
# The checks the target's protection names are the requirement, read from that branch: five green out of
# twenty-two is not a green head. The protection is read per target, so a rule that holds for dev and not
# for master is applied where it belongs.
#
# A commit whose required checks all reported, all success, is green. A commit where some reported success
# and some were skipped is skipped: the skip says the commit did not touch what that check covers, so the
# commit cannot stand on its own. Trust passes down the chain: from the head backwards, the first green
# commit vouches for the skipped ones above it, and nothing else does. A red, or a required check that never
# reported, stops the move at the commit it is on.
#
# The walk is short by design: ten commits in a row whose required checks were all skipped say the branch
# was not run through a pull request, and ten is where that stops being plausible. Beyond it the message says
# what to do - a pull request on the branch runs the checks, and the next move finds them.
#
# A push made with GITHUB_TOKEN does not go through the branch protection, so this script is the barrier a
# fast-forward leans on, not a second opinion on one GitHub already applied.
set -euo pipefail

repository="$1" target="$2" source="$3" minutes="${4:-40}"

# The most commits in a row whose required checks were all skipped before the chain stops being trusted.
# Ten is where a branch that was never run through a pull request stops looking like an accident.
limit=10

fail() { echo "::error::$1"; exit 1; }

# One line per check of a commit: "<name>\t<status>\t<conclusion>"; a check not started has no conclusion.
checks() {
    gh api "repos/$repository/commits/$1/check-runs?per_page=100" --paginate 2>/dev/null \
        | jq -r -s '.[].check_runs[] | "\(.name)\t\(.status)\t\(.conclusion // "-")"' \
        | tr -d '\r'
}

git fetch -q origin "+refs/heads/$target:refs/remotes/origin/$target" "+refs/heads/$source:refs/remotes/origin/$source" \
    || fail "cannot read $target and $source from origin"
git merge-base --is-ancestor "origin/$target" "origin/$source" \
    || fail "$target is not an ancestor of $source: the move cannot be a fast-forward; rebase $source onto $target"

mapfile -t commits < <(git rev-list --reverse "origin/$target..origin/$source")
[ "${#commits[@]}" -gt 0 ] || fail "$source carries nothing that $target does not have"

echo "$source brings ${#commits[@]} commit(s) into $target"

# The checks the protection names, read before any judgement: without them "green" cannot be told from
# "half the checks never ran".
required=$(gh api "repos/$repository/branches/$target/protection/required_status_checks" \
    --jq '.contexts[]' 2>/dev/null || true)
[ -n "$required" ] || fail "the protection of $target names no required status checks: a green head cannot be told from a red one"
echo "the protection of $target names $(grep -c . <<< "$required") check(s)"

# What one commit is: "green", "skipped", "red", "incomplete" or "running", and what is wrong with it.
verdict() {
    local sha="$1" output missing="" red="" notgreen="" running=0
    output=$(checks "$sha")
    while read -r name; do
        [ -n "$name" ] || continue
        line=$(awk -F'\t' -v n="$name" '$1 == n' <<< "$output" | head -1)
        if [ -z "$line" ]; then
            missing+="    $name: not started"$'\n'
            continue
        fi
        status=$(cut -f2 <<< "$line") conclusion=$(cut -f3 <<< "$line")
        if [ "$status" != "completed" ]; then
            running=1
            continue
        fi
        case "$conclusion" in
            success) ;;
            skipped|neutral) notgreen+="    $name: $conclusion"$'\n' ;;
            *) red+="    $name: $conclusion"$'\n' ;;
        esac
    done <<< "$required"

    # A check the protection does not name: a red one blocks like a required red.
    while IFS=$'\t' read -r name status conclusion; do
        [ -n "$name" ] || continue
        grep -qxF "$name" <<< "$required" && continue
        if [ "$status" = "completed" ] && [ "$conclusion" != "success" ] && [ "$conclusion" != "skipped" ] && [ "$conclusion" != "neutral" ]; then
            red+="    $name: $conclusion (not required)"$'\n'
        elif [ "$status" != "completed" ]; then
            running=1
        fi
    done <<< "$output"

    [ -n "$red" ] && { printf 'red\n%s' "$red"; return; }
    [ -n "$missing" ] && { printf 'incomplete\n%s' "$missing"; return; }
    [ "$running" -eq 1 ] && { printf 'running\n'; return; }
    [ -n "$notgreen" ] && { printf 'skipped\n%s' "$notgreen"; return; }
    printf 'green\n'
}

# The walk: from the head backwards, skipped commits are vouched for by the first green under them.
span=0
for (( i=${#commits[@]}-1; i>=0; i-- )); do
    sha=${commits[$i]} short=${sha:0:7}

    for minute in $(seq 1 "$minutes"); do
        result=$(verdict "$sha")
        state=${result%%$'\n'*}
        [ "$state" = "running" ] || break
        [ "$minute" -lt "$minutes" ] || fail "checks of $short are still running after $minutes minute(s)"
        sleep 60
    done
    detail=${result#*$'\n'}

    case "$state" in
        green)
            echo "$short: green, the $((${#commits[@]} - 1 - i)) commit(s) above it are vouched for"
            exit 0
            ;;
        skipped)
            span=$((span + 1))
            if [ "$span" -gt "$limit" ]; then
                fail "$short is skipped, and $span commit(s) above it bring no green check: too long a chain to trust. "
                fail "Open a pull request on this branch so the checks run on it, then run this again; "
                fail "an atomic branch is one to seven commits, and this one is longer (git-style). "
            fi
            echo "$short: skipped, looking below it"
            ;;
        red)
            echo "::error::$short did not pass:"
            printf '%s' "$detail"
            exit 1
            ;;
        incomplete)
            echo "::error::$short has required checks that never reported:"
            printf '%s' "$detail"
            exit 1
            ;;
    esac
done

fail "no commit of $source carries a green check: open a pull request on the branch so the checks run on it, then run this again"
