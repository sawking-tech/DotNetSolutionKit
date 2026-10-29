#!/usr/bin/env python3
"""Checks the metadata keys of the JSON files of a generated solution (ADR-006).

    scripts/check-json-metadata.py <solution>

A key that starts with `_` is metadata: `_comment_<Name>` belongs to the field `<Name>` of its object, which
must be there, `_note_<Name>` to a topic of the object, `_<kind>` to the object. A field left empty on
purpose - "" or [] - must say what goes there, with `_comment_<Key>`. Exits 1 with a line per finding.
"""

import json
import os
import sys

METADATA = "_"
COMMENT = "_comment_"
IGNORED_FOLDERS = {"bin", "obj", ".git", "node_modules", ".dotskit"}


def json_files(root):
    for folder, folders, files in os.walk(root):
        folders[:] = [f for f in folders if f not in IGNORED_FOLDERS]
        for name in files:
            if name.endswith(".json"):
                yield os.path.join(folder, name)


def is_empty(value):
    return value == "" or value == []


def check_object(obj, where, findings):
    for key, value in obj.items():
        if key.startswith(COMMENT) and key[len(COMMENT):] not in obj:
            findings.append(f"{where}: {key} has no field {key[len(COMMENT):]}; a note on a topic is _note_{key[len(COMMENT):]}")
        if key.startswith(METADATA):
            continue
        if is_empty(value) and COMMENT + key not in obj:
            findings.append(f"{where}: {key} is empty and has no {COMMENT}{key}")
        if isinstance(value, dict):
            check_object(value, f"{where}.{key}", findings)


def main(root):
    findings = []
    for path in sorted(json_files(root)):
        try:
            with open(path, encoding="utf-8-sig") as file:
                data = json.load(file)
        except ValueError:
            continue  # not JSON this check reads, as a launch profile with comments
        if isinstance(data, dict):
            check_object(data, os.path.relpath(path, root).replace(os.sep, "/"), findings)
    for finding in findings:
        print(finding)
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
