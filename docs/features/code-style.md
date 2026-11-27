# Code style in a solution

Generated with `--code-style`, off by default. Pass it with `--solution`.

A solution keeps one file of style rules, `.editorconfig` at its root. The flag writes the template's own
rules there, every rule at `suggestion`, and the file is the team's from that moment: they edit it, raise
what they want held, and add rules of their own. `dotskit` reads that file to lay out the code an upgrade
brings, so the code arrives in the style the team already writes.

## Why the file rather than a formatter setting

An upgrade writes files the team never saw: a module that moves to another project, a contract that gains a
field, a test that follows its capability. Written in the template's own style - four spaces, braces on their
own line, usings outside the namespace - they mix two layouts in one project and turn every merge into a
conflict about whitespace.

The file solves it once. The team states its style in the place every .NET editor and `dotnet format`
already read, and the tool has something to lay the incoming code out by. Without the file, the tool has no
style to follow and leaves the template's bytes alone.

`.editorconfig` is read by the editors and by the SDK, so the file serves the team outside an upgrade too:
the IDE formats new code by it, and a rule raised to `warning` fails their build on a violation.

## What the flag writes

`template/.editorconfig.template` - the rules of the template's own repository, every one at `suggestion`.
`suggestion` says what the file is: a starting point. The team's build does not fail on our rules until the
team raises them, and the rule groups are the same as [the repository's own](../architecture/code-style.md),
which keeps the two files from drifting apart.

A rule at `suggestion` is still applied by the tool on an upgrade. The severity states what the team's build
does with our style; it does not mean the team wants another layout.

## The merge, and the formatting an upgrade does

The merge is a three-way one: the output of the template the solution came from, the solution as the team
has it, and the output of the template it goes to. Style enters before the merge, on the template's side
only.

1. **Both sides of the template are laid out by the team's rules.** The output the solution came from and the
   output it goes to, each in a copy of its own, formatted by the union of the two files: the template's
   rules, then the team's after them, so the team's rule wins where both name one. The solution's files are
   never written.
2. **Why both, and not only the new one.** git compares each side with the base. A base left in the
   template's style against a team that reformats every line reads as every line changed by the team, and the
   merge then reports a conflict on each. Laid out on both sides, the two agree on the style and differ only
   by what the template actually changed.
3. **The formatter runs twice.** `dotnet format whitespace` applies the layout - the braces, the indent, the
   spacing. `dotnet format style --severity info` applies the code rules a layout pass leaves alone - the
   usings, the namespace, the blank lines. Neither pass covers the other; a single pass leaves half the style
   in the template's shape.
4. **The union lives beside the team's file, not over it.** It is written as `dotskit.editorconfig` next to
   the output's own `.editorconfig`, and deleted after the run: a file the merge expects must not vanish, and
   the team's file is theirs.
5. **A rule at `none` stays off.** The formatter is given `--severity info`, the threshold at which a rule at
   `suggestion` applies. A team that turned a rule off in its own file keeps it off.

Only the files both sides changed are formatted. A file the team alone touched is the team's, and the merge
never writes it.

## A solution with a file of its own

A solution that carries an `.editorconfig` keeps it. The flag is for a solution without one; the upgrade
adds nothing to a file that is already there, and the team's rules are what the formatting follows.

## What it costs

- The merge runs `dotnet format` up to four times: two passes for the output the solution came from, two for
  the one it goes to. On a large solution that is the slowest part of an upgrade, and it is why the
  formatting runs on the files both sides changed, not on the whole tree.
- The tool formats a copy and merges that. The solution's files are not laid out by the upgrade; a file whose
  layout bothers the team is still the team's to format.
- The style follows the rules a file names. A rule the team adds that the installed SDK does not know is
  reported by the formatter and ignored.

## Related

- [Code style](../architecture/code-style.md) - the template's own rules and how CI holds them.
- [ADR-007](../adr/007-code-style.md) - why one style, owned by the formatters.
- [Upgrading](tool.md) - the upgrade as a whole, and where the formatting sits in it.
