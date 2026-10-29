#!/usr/bin/env python3
"""Checks the bytes of every text file git tracks against .editorconfig, the template's sources among them.

    scripts/check-style.py [--fix]

A file is UTF-8 with LF line ends, ends with a line end and has no space or tab at the end of a line nor a tab
in its indent; a byte order mark only a solution file has, and it must. Code has no Cyrillic outside a test
project, where it is test data. With --fix the byte order mark, the line ends and the spaces at the ends of
lines are brought to it in place; a tab in an indent and Cyrillic a person rewrites. Exits 1 with a line per
finding, and 2 with one line when it runs outside a git repository.
"""

import os
import re
import subprocess
import sys

BOM = b"\xef\xbb\xbf"
# Visual Studio writes a solution file with a byte order mark and indents it with tabs (.editorconfig).
WITH_BOM_AND_TABS = (".sln",)
# A NUL among the first bytes marks a binary file, as git takes it.
BINARY_PROBE = 8000
# Code, its comments and its names are in English (CLAUDE.md); a test project may hold Cyrillic test data.
CODE = (".cs", ".py", ".sh", ".yml", ".yaml", ".csproj", ".props", ".targets")
TEST_PROJECT = re.compile(r"(^|/)[^/]*\.Tests/")
CYRILLIC = re.compile("[\u0400-\u04ff]")
LINE_END = re.compile(rb"\r\n|\r|\n")
NOT_UTF8 = "is not UTF-8: save it as UTF-8"


def repository_root():
    found = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True)
    return found.stdout.strip() if found.returncode == 0 else None


def tracked_files():
    listed = subprocess.run(["git", "ls-files", "-z"], capture_output=True, check=True).stdout
    return [path for path in listed.decode("utf-8").split("\0") if path and os.path.isfile(path)]


def is_binary(data):
    return b"\0" in data[:BINARY_PROBE]


def takes_bom_and_tabs(path):
    return path.endswith(WITH_BOM_AND_TABS)


def may_hold_cyrillic(path):
    return not path.endswith(CODE) or TEST_PROJECT.search(path) is not None


def body_of(data):
    return data[len(BOM):] if data.startswith(BOM) else data


def findings(path, data):
    """What the file breaks of the style, in words; nothing for a binary or an empty file."""
    if is_binary(data) or not data:
        return []
    body = body_of(data)
    try:
        text = body.decode("utf-8")
    except UnicodeDecodeError:
        return [NOT_UTF8]
    found = []
    if takes_bom_and_tabs(path) != data.startswith(BOM):
        found.append("needs a byte order mark" if takes_bom_and_tabs(path) else "has a byte order mark")
    if b"\r" in body:
        found.append("has CR line ends")
    if not body.endswith(b"\n"):
        found.append("does not end with a line end")
    lines = LINE_END.split(body)
    if any(line != line.rstrip(b" \t") for line in lines):
        found.append("has spaces at the end of a line")
    if not takes_bom_and_tabs(path) and any(b"\t" in line[:len(line) - len(line.lstrip(b" \t"))] for line in lines):
        found.append("has a tab in an indent")
    if not may_hold_cyrillic(path) and CYRILLIC.search(text):
        number = next(n for n, line in enumerate(lines, 1) if CYRILLIC.search(line.decode("utf-8")))
        found.append(f"line {number} has Cyrillic: code and comments are in English")
    return found


def fixed(path, data):
    """The file with its byte order mark, line ends and spaces at the ends of lines as the style asks."""
    if is_binary(data) or not data or NOT_UTF8 in findings(path, data):
        return data
    lines = LINE_END.split(body_of(data))
    if lines[-1] == b"":
        lines.pop()
    text = b"\n".join(line.rstrip(b" \t") for line in lines) + b"\n"
    return (BOM if takes_bom_and_tabs(path) else b"") + text


def main(fix):
    root = repository_root()
    if root is None:
        print("check-style.py runs in a git repository: run it from a clone of DotNetSolutionKit")
        return 2
    os.chdir(root)
    left = 0
    for path in tracked_files():
        with open(path, "rb") as file:
            data = file.read()
        if fix and fixed(path, data) != data:
            data = fixed(path, data)
            with open(path, "wb") as file:
                file.write(data)
        for finding in findings(path, data):
            print(f"{path}: {finding}")
            left += 1
    return 1 if left else 0


if __name__ == "__main__":
    sys.exit(main("--fix" in sys.argv[1:]))
