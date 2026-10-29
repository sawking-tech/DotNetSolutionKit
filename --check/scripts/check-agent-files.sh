#!/usr/bin/env bash
#
# Checks the rules and skills for an AI agent in a generated solution:
#
#   scripts/check-agent-files.sh <solution> <agent>   # <agent>: claude, opencode or none, as passed to --Agent
#
# The format's own files are there and the other format's are not, inside the texts too; every skill has
# the frontmatter both agents read (only name and description, the name matching its folder); no template
# placeholder is left in them.
set -euo pipefail

solution="$1" agent="$2"
cd "$solution"

failed=0
fail() { echo "::error::$1"; failed=1; }

case "$agent" in
    claude) rules=CLAUDE.md folder=.claude other_rules=AGENTS.md other_folder=.opencode ;;
    opencode) rules=AGENTS.md folder=.opencode other_rules=CLAUDE.md other_folder=.claude ;;
    none)
        for path in CLAUDE.md AGENTS.md .claude .opencode; do
            [ ! -e "$path" ] || fail "--Agent none generated $path"
        done
        [ "$failed" -eq 0 ] && echo "No agent files, as --Agent none asks."
        exit "$failed"
        ;;
    *) echo "unknown agent: $agent" >&2; exit 2 ;;
esac

[ -f "$rules" ] || fail "$rules is missing"
[ ! -e "$other_rules" ] || fail "$other_rules is there with --Agent $agent"
[ ! -e "$other_folder" ] || fail "$other_folder is there with --Agent $agent"

skills=0
for skill in "$folder"/skills/*/SKILL.md; do
    [ -f "$skill" ] || continue
    skills=$((skills + 1))
    name=$(basename "$(dirname "$skill")")
    keys=$(awk 'NR == 1 && $0 != "---" { exit } NR > 1 && $0 == "---" { exit } NR > 1 { sub(/:.*/, ""); print }' "$skill" | tr '\n' ' ')
    [ "$keys" = "name description " ] || fail "$skill: the frontmatter has '$keys', both agents read only 'name description'"
    grep -qx "name: $name" "$skill" || fail "$skill: its name is not its folder, $name"
done
[ "$skills" -gt 0 ] || fail "no skills in $folder/skills"

texts=("$rules" "$folder")
if grep -rlE "NamespaceRoot|ProductName|ServiceNameOrCustom" "${texts[@]}"; then
    fail "a template placeholder is left in the files above"
fi
if grep -rlF -e "$other_folder/" -e "$other_rules" "${texts[@]}"; then
    fail "the files above name the other format ($other_folder/, $other_rules)"
fi
grep -qxF "$folder/session-context/" .gitignore || fail ".gitignore does not keep $folder/session-context/ out"

[ "$failed" -eq 0 ] && echo "$rules and $skills skills in $folder/skills, for $agent."
exit "$failed"
