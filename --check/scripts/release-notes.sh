#!/usr/bin/env bash
#
# Release notes of one version of the template, from version.json, as Markdown:
#
#   scripts/release-notes.sh 2.1.0
#   scripts/release-notes.sh 2.1.0 plain   # the same as plain text, for the package on nuget.org
#
# The release workflow and a release made by hand print the same text.
set -euo pipefail

version="$1" format="${2:-markdown}"
cd "$(dirname "$0")/.."

jq -e --arg v "$version" '.releaseNotes[$v]' version.json >/dev/null \
    || { echo "version.json has no release notes for $version" >&2; exit 1; }

jq -r --arg v "$version" '
    .releaseNotes[$v] as $notes
    | ([$notes.headline, ""] + ($notes.highlights | map("- " + .)) | join("\n"))
' version.json

upgrading="https://github.com/${GITHUB_REPOSITORY:-sawking-tech/DotNetSolutionKit}/blob/v$version/docs/getting-started/upgrading.md"
if [ "$format" = plain ]; then
    printf '\nHow to update a solution generated from an earlier version: %s\n' "$upgrading"
else
    printf '\nHow to update a solution generated from an earlier version: [upgrading](%s).\n' "$upgrading"
fi
