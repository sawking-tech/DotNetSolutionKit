"""Makes a generated OpenAPI document stable, so a diff of two of them shows only contract changes.

A service documents itself with details that change from machine to machine and release to release:
a title naming the environment, a version that follows the release, server URLs, line endings from
whichever machine compiled the XML documentation. Diffing the raw documents would report churn
nobody can observe.
"""
import io
import json
import sys
from collections import OrderedDict

# Health endpoints belong to operations, not to a service's contract.
HEALTH_PATHS = {"/health", "/ready"}


def unify_newlines(value):
    """Line endings inside descriptions, as one platform rather than whichever built the document.

    Summaries and descriptions come from the generated XML documentation file, which carries the
    line endings of the machine that compiled it: \\r\\n on Windows, \\n on Linux. They end up inside
    JSON string values, so the same contract produced on a developer's machine and on the build
    agent differs in every multi-line description.
    """
    if isinstance(value, str):
        return value.replace("\r\n", "\n").replace("\r", "\n")
    if isinstance(value, list):
        return [unify_newlines(item) for item in value]
    if isinstance(value, dict):
        return OrderedDict((key, unify_newlines(item)) for key, item in value.items())
    return value


def normalise(document: dict) -> dict:
    info = document.get("info", {})
    # "... : Local" - the environment the generator ran in. Stable across machines is what matters.
    if isinstance(info.get("title"), str):
        info["title"] = info["title"].split(" : ")[0].strip()
    # The document version tracks the release, so leaving it in would make every bump look like a
    # contract change. A fixed marker keeps the field valid and the diffs quiet.
    info["version"] = "public"
    # Server URLs are per-environment by construction.
    document.pop("servers", None)

    paths = document.get("paths") or {}
    document["paths"] = OrderedDict(
        (path, paths[path])
        for path in sorted(paths)
        if path.rstrip("/").lower() not in HEALTH_PATHS)

    components = document.get("components") or {}
    if "schemas" in components:
        components["schemas"] = OrderedDict(sorted(components["schemas"].items()))

    return document


def main() -> None:
    source, destination = sys.argv[1], sys.argv[2]

    with io.open(source, encoding="utf-8-sig") as handle:
        document = json.load(handle, object_pairs_hook=OrderedDict)

    document = normalise(unify_newlines(document))

    # Sorted keys and a trailing newline so a diff shows the change, not a reshuffle.
    with io.open(destination, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(document, handle, indent=2, ensure_ascii=False, sort_keys=False)
        handle.write("\n")

    print(f"    {len(document['paths'])} paths -> {destination}")


if __name__ == "__main__":
    main()
