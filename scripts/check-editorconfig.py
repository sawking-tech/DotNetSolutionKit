#!/usr/bin/env python3
"""Holds template/.editorconfig.template to the root .editorconfig: the same rules and values, offered, not applied.

    scripts/check-editorconfig.py

Two files state the template's style: the root `.editorconfig`, which holds the repository and its CI, and
`template/.editorconfig.template`, which the package carries and `--CodeStyle` writes into a solution. They must not
drift: the merge of an upgrade formats the code by the rules of the second, and the CI of the template by the first.

The diff allowed between them, and the whole of it:

- a severity of `warning` in the root is a `suggestion` in the solution's copy: the style arrives offered, and a
  build that fails on it is the team's decision, made by raising the severity in its own file (issue #247);
- no section for `template/**.cs`: a solution has no `template/` folder;
- the comments differ: one file speaks to a team, the other to the people who hold the repository.

Exits 1 with a line per difference: a rule the solution's copy lacks, has twice, or holds with another value.
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / ".editorconfig"
TARGET = ROOT / "template" / ".editorconfig.template"

SECTION = re.compile(r"^\[(.+)\]\s*$")
PAIR = re.compile(r"^\s*([^=#][^=]*?)\s*=\s*(.+?)\s*$")
# A section the solution's copy has no use for.
TEMPLATE_ONLY = ("template/**.cs",)
# The severities the solution's copy offers instead of applying.
LOWERED = {"warning": "suggestion"}


def rules(text: str) -> dict[tuple[str, str], str]:
    """Every rule by its section and its name; a section is read from its header to the next."""
    found: dict[tuple[str, str], str] = {}
    section = ""
    for line in text.split("\n"):
        if header := SECTION.match(line):
            section = header.group(1)
            continue
        if pair := PAIR.match(line):
            found[(section, pair.group(1))] = pair.group(2)
    return found


def differences(source: dict[tuple[str, str], str], target: dict[tuple[str, str], str]) -> list[str]:
    found = []
    for (section, name), value in source.items():
        if any(section.startswith(only) for only in TEMPLATE_ONLY):
            continue
        if (section, name) not in target:
            found.append(f"[{section}] {name} = {value} is missing")
        elif target[(section, name)] != LOWERED.get(value, value):
            found.append(f"[{section}] {name} = {target[(section, name)]}, the root says {value}")
    for (section, name), value in target.items():
        if (section, name) not in source or any(section.startswith(only) for only in TEMPLATE_ONLY):
            found.append(f"[{section}] {name} = {value} is not in the root")
    return found


def main() -> int:
    found = differences(rules(SOURCE.read_text(encoding="utf-8")), rules(TARGET.read_text(encoding="utf-8")))
    for line in found:
        print(line)
    if found:
        print(f"{TARGET.relative_to(ROOT)} and .editorconfig differ: bring the rules of the two together")
        return 1

    print("template/.editorconfig.template holds the rules of .editorconfig, offered")
    return 0


if __name__ == "__main__":
    sys.exit(main())
