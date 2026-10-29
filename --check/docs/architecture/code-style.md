# Code style

The rules of every file of the repository and how they are held. Why the style belongs to the formatters, and where
the rules come from: [ADR-007](../adr/007-code-style.md).

The rules are in `.editorconfig` at the root. It is outside `template/`, so the package and a generated solution do
not carry it. A rule is in one of two layers:

- applied: a formatter fixes it with no person, and a build or CI fails on it (`warning`);
- offered: no formatter applies it; the IDE shows it (`suggestion`) or it is off (`none`), and no build fails.

The groups of `.editorconfig` go in the order of the sections below, from the basic rules to those of taste, and
each rule has a comment. A new rule is best put into its group with a comment; this is a wish, and nothing checks
it.

## Bring a change to the style

```bash
python3 scripts/check-style.py --fix                  # the bytes of every tracked file
dotnet format tool/DotsKit/DotsKit.csproj             # the C# of the tool
dotnet format tool/DotsKit.Tests/DotsKit.Tests.csproj # and of its tests: a project formats only itself
```

CI runs the same without fixing: the job `One style` checks the bytes of every file, and the job of dotskit builds
the tool with `-warnaserror` and runs `dotnet format --verify-no-changes`. The format step is needed beside the
build: a build on SDK 8 skips some rules, IDE0130 among them.

## The rules, from the basic to those of taste

### Bytes of every file

| Rule | Value | Why |
|---|---|---|
| `charset` | `utf-8`; `utf-8-bom` for `.sln` | one encoding; Visual Studio writes a solution file with a byte order mark |
| `end_of_line` | `lf` | `.gitattributes` makes every text file LF in every checkout, so a Windows clone has the same bytes |
| `insert_final_newline` | `true` | a file ends with a line end |
| `trim_trailing_whitespace` | `true` | no spaces or tabs at the end of a line |
| `indent_style`, `indent_size` | spaces; 4 in `.cs`, `.csproj`, `.sh`, `.py`, 2 elsewhere; tabs in `.sln` | the size each kind of file had in most of the repository |

`scripts/check-style.py` holds the bytes of these on every file git tracks, the template's sources among them: the
charset, the line ends, the line end at the end, no spaces at the ends of lines, the byte order mark, and no tab in
an indent where spaces are asked. The size of an indent it does not check; in C# `dotnet format` holds it. It also
finds Cyrillic in code outside a test project: code and its comments are in English.

### Layout of C# (IDE0055, applied)

Braces on a line of their own for every construct, `else`, `catch` and `finally` on a new line, one statement on a
line, the default spacing. How an initializer or a collection is laid out over lines is left to the author
(`csharp_new_line_before_members_in_object_initializers = false`): with `true` the formatter puts every element of a
multi-line collection on a line of its own, which makes a list of arguments unreadable.

### Usings and namespaces

| Rule | Value | Layer | Why |
|---|---|---|---|
| Order of usings | `System` first, then by name, no blank line between groups | applied | as Microsoft's sources sort them |
| Placement | outside the namespace | applied | the C# coding conventions |
| An unused using (IDE0005) | removed | applied | needs `GenerateDocumentationFile` to run on a build |
| Namespace (IDE0161) | file-scoped | applied | its scope is the file and seen at once |
| Namespace and folder (IDE0130) | match | applied | a type is found where its namespace says |

### Blank lines

At most one blank line in a row (IDE2000), no blank line between two closing braces (IDE2002), one statement on a
line (IDE2001); all applied. A statement may follow a block with no blank line (IDE2003 off), and `=>` stays at the
end of its line (IDE2006 off: the other way moves it to the next line). Python keeps the two blank lines PEP 8 asks
between top-level definitions.

### The language

| Rule | Value | Layer | Why |
|---|---|---|---|
| Braces (IDE0011) | around the body of every `if`, `else`, `for`, `foreach`, `while`, `using`, `lock` | applied | the scope of a body is seen, never inferred from the indent |
| `this.` | not written | applied | the C# coding conventions |
| Type names | `int` over `Int32` | applied | the language's keywords |
| Accessibility | always written, but on interface members | applied | |
| Using statement (IDE0063) | the IDE does not offer the declaration form | off | the end of a using declaration is not seen; no formatter turns one back, so the code keeps the ones it has |
| Constructors (IDE0290) | primary or explicit, the author's | off | moving the template's 117 primary constructors would give teams real conflicts |

### Names (IDE1006, offered)

Constants in `PascalCase`; `private static readonly` fields in `PascalCase`, as StyleCop SA1311 and JetBrains have
it; other private fields in `_camelCase`. No formatter renames, so the rules are offered and never fail a build.
Microsoft's own `s_` prefix is not taken: [ADR-007](../adr/007-code-style.md).

### Taste

| Rule | Value | Why |
|---|---|---|
| `var` (IDE0007, IDE0008) | neither required | the author's |
| Expression or block body (IDE0021-IDE0027, IDE0053, IDE0061) | neither required | the author's |
| `?:` and every switch case (IDE0045, IDE0046, IDE0072) | offered | the IDE offers the rewrite and leaves it to the author |
| A discarded result (IDE0058) | off | `_ =` before every call that returns a value is noise |
| Collection expressions (IDE0300-IDE0306) | offered | no formatter applies them, and IDE0305 comes with SDK 10 only |

### Quality rules and documentation

Every analyzer rule of .NET is at `warning` (`dotnet_analyzer_diagnostic.severity`), and `AnalysisLevel` 8.0 keeps
the set of .NET 8 on a newer SDK. The exemptions:

- the rules of globalization are offered: the tool reads files and the output of git, not text for people;
- CA1863, caching a composite format, is offered: the tool has no hot path;
- JSON002 is off: a JSON string of a test is the data of the file it goes to;
- at two places a rule is turned off with its reason in `tool/DotsKit/Suppressions.cs`: CA1822 on a `_comment_` key
  of ADR-006, which System.Text.Json writes only as an instance property, and CA1001 on a holder of a
  `SemaphoreSlim` that lives as long as the process.

An XML doc comment describes every parameter of its member (CS1573 at `warning`): one that describes some of them
reads as if it described all.

### JSON

The metadata keys of a JSON file, `_comment_<Key>` and `_note`: [ADR-006](../adr/006-json-metadata-key-convention.md).
`scripts/check-json-metadata.py` checks them on a generated solution.

### The template's sources

`[template/**.cs]` turns the analyzers off for an IDE that opens the template's sources: they hold `//#if` at the
first column and placeholder names, and no project of the repository compiles them. `dotnet format` is not run on
them; their bytes are held by `check-style.py`, and their C# is to be checked on the solutions CI generates.

## Planned

A pre-commit hook for the bytes and the layout of the files of a commit; the four line patterns of StyleCop about
blank lines by a brace or a doc comment in `check-style.py`; the length of a line, 99 in Python, 120 in YAML and
Markdown; `dotnet format` on the solutions CI generates. See [ADR-007](../adr/007-code-style.md), Planned.
