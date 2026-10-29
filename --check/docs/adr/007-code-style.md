# ADR-007: One code style, owned by the formatters

**Status:** Accepted, 2026-10-29

**Author:** Vladimir Savkin, the creator of DotNetSolutionKit

## Context

Most of the repository's code is generated with an AI model used as a tool, as
[Working with AI agents](../getting-started/working-with-ai-agents.md) describes: the author makes the decisions,
lays down the architecture and the rules, the model generates the code, and the author checks the result and answers
for it. Until 2.9 the style of that code was held by the words of CLAUDE.md alone. A probabilistic generator, by
its nature, gives no guarantee that its output keeps to rules written in words. A formatter does, it is cheap to add, and its
output is deterministic, which an upgrade needs too. CI built with `-warnaserror` and
checked nothing else. The template's sources mixed byte order
marks, files with no line end at the end and spaces at the ends of lines, and a generated solution got them as
they were: `dotnet new` copies the bytes of a source file.

The style of the template reaches the teams too. A team upgrades its solution with
`dotskit upgrade`, which merges what the template changed into what the team changed. A change of style in a file
the team also edited is a change of lines, and the merge shows it as a conflict where nobody changed what the code
does. The first CI run of this decision showed it: the template gained a line end at the end of `Program.cs`, a
test had added a line of the team there, and git reported a conflict.

## Decision

The formatters own the style. What they do is the style; nobody changes a file by hand for its style, in
dotskit.template or in dotskit.tool. A person changes logic, and a change of logic is written in the style from its
first line, so the formatters have nothing to change.

The rules sit in one `.editorconfig` at the root of the repository. It is outside `template/`, so the package, which
is packed from `template/`, and a generated solution never carry it; a solution gets the style only by a flag of its
own, off by default (planned, see below).

The rules are in two layers.

- Rules the formatters apply: the bytes of every file (UTF-8, LF, a line end at the end, no spaces at the ends of
  lines, no byte order mark but in a solution file), the layout of C#, braces around every body, the order of the
  usings, blank lines. These rules are at `warning`: a build with `-warnaserror` fails on them, and a formatter fixes
  them with no person.
- Rules no formatter applies: names, collection expressions, the using statement, primary constructors. A rule a
  formatter cannot apply is never above `suggestion`: the IDE offers it, no build fails on it, and no person rewrites
  code for it.

The XML documentation (CS1573) and the quality rules (CA) are not style: they speak of what the code says and
does, and they stay at `warning` in the tool.

The style of the template's files changes only by the pack of tools: `scripts/check-style.py --fix` for the bytes and
`dotnet format` with the root `.editorconfig`. A version of the template is, in its style, the pack applied to the
version before it, so `dotskit upgrade` can apply the same pack to the team's side and the change of style meets
nothing. Its first part is in place: both merges of `dotskit` bring the three sides of a file to the bytes of the
style before they merge.

## Where the rules come from

The rules follow the mainstream of each language, read from its sources, not from memory.

| Area | Sources |
|---|---|
| C# | [C# coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions), the `.editorconfig` of [dotnet/runtime](https://github.com/dotnet/runtime/blob/main/.editorconfig) and [dotnet/roslyn](https://github.com/dotnet/roslyn/blob/main/.editorconfig), [StyleCop.Analyzers](https://github.com/DotNetAnalyzers/StyleCopAnalyzers), the defaults of JetBrains Rider and ReSharper |
| How the tools fit | [Code analysis in .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview), [code-style rule options](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/code-style-rule-options), [dotnet format](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format) |
| Python | [PEP 8](https://peps.python.org/pep-0008/) |
| Shell | [ShellCheck](https://github.com/koalaman/shellcheck) |
| JSON | [ADR-006](006-json-metadata-key-convention.md), the metadata keys |

With Microsoft the rules agree on Allman braces, usings outside the namespace with `System` first, file-scoped
namespaces, the language's keywords over the names of the framework's types, and one statement on a line. They part
from Microsoft in three places, each with who stands on the other side:

| Rule | Microsoft | Here | With |
|---|---|---|---|
| A `private static readonly` field | `s_camelCase` | `PascalCase` | StyleCop SA1311 (and SA1308 forbids `s_`), the defaults of JetBrains |
| `var` | when the type is apparent | the author's choice | dotnet/runtime, which permits `var` and does not require it |
| Braces | dotnet/runtime: may be left out when every block of the statement is one line | always | the reason below |

Braces go around the body of every `if`, `else`, `for`, `foreach`, `while`, `using` and `lock`: the scope of a body
is seen at a glance and never inferred from the indent. For the same reason the IDE does not offer to turn a `using`
block into a using declaration, whose end no line shows; the declarations the code has stay, since no formatter
turns them back. A file-scoped namespace is kept: its scope is the file.

[Code style](../architecture/code-style.md) gives every rule with its value and its reason.

## Alternatives considered

- StyleCop.Analyzers as a package. Most of its rules repeat a rule of the IDE; its defaults (`this.`, usings
  inside the namespace) contradict the other sources; with no exemption it reports 1 357 findings in the tool. The
  four of its rules worth having here (SA1505, SA1506, SA1508, SA1517: blank lines by a brace, after a doc comment
  or at the start of a file) are patterns of lines a check of the text can hold on every file, the template's
  sources among them.
- The `s_` prefix, as Microsoft writes its own code. Only Microsoft's sources back it, and they are one author.
- A `global.json` pinning the SDK to 8.0, so the rules are the same on every machine. The tests of the tool
  generate solutions outside the repository; a newer SDK builds them there, inherits the MSBuild of the SDK 8 that
  runs the tests, and fails. `AnalysisLevel` 8.0 keeps the quality rules of .NET 8 instead, at no such cost.
- Rules a person applies, such as explicit constructors or using blocks across the code. Each would be a change
  of many files by hand, a change of the template's files no tool can repeat on a team's copy, and for primary
  constructors 117 classes of the template a team edits most.

## Cost

A layout is taken as the formatter gives it, also where a person would lay it out better, and no rule of taste is
kept that a formatter does not apply. In return a change of style between two versions of the template is made by a
tool and can be repeated by a tool, so the upgrade of a team goes smoother and breaks less often.

## Planned

These follow this decision and are not in place yet:

- a pre-commit hook that applies the bytes and the layout to the files of a commit;
- the four line patterns of StyleCop in `check-style.py`;
- `dotnet format` checked on the solutions CI generates, and the template's sources brought to it by the pack;
- `dotskit upgrade` applying the whole pack, the bytes and `dotnet format`, to the sides of a merge, and then the
  team's own `.editorconfig` over it;
- the flag that gives a solution this `.editorconfig` at the level `suggestion`.
