# dotskit

`dotskit` is the command-line tool of DotNetSolutionKit. It generates a solution with the template, adds
services to it and flags to its services, and keeps what the team changed: where the team and the template
changed the same file, it merges the two the way git merges branches.

The template works without the tool, as [generating a solution](generating-a-solution.md) describes. The
tool adds what one run of `dotnet new` cannot do: add a flag to a service that has its folder already,
update a file the team has changed, and record in the solution what it was generated from.

## Install

```bash
dotnet tool install -g SawKing.DotsKit.Tool
```

The tool is published from version 2.8. dotskit X.Y.Z generates with the template X.Y.Z: it installs the
package `SawKing.DotNetSolutionKit` of its own version into a hive of its own, and the template installed
by `dotnet new install` is left as it is.

## Commands

```bash
# in an empty folder: a solution with its first service
dotskit new -N MyCompany -P MyProduct -S Orders

# in the solution: one more service, with the solution's names, database and deployment
dotskit new -S Billing --Storage true

# in the solution: a flag for a service it has
dotskit new -S Orders --MongoDB true

# in a solution made without dotskit: describe it in its manifest
dotskit init

# in a solution: bring it to the tool's version of the template
dotskit upgrade
```

`dotskit new` reads from the folder whether to make a solution or to add to one: it looks for
`src/services/*.All.sln` in the folder and its parents. `--Solution` is not needed and changes nothing.

The template's parameters are passed as they are, in short or long form, in any case: `-S`, `--Storage`,
`--storage`, `--api-gateway`. See the [parameters](generating-a-solution.md#parameters).

## What a command does

1. Checks the folder. A new solution goes into an empty folder or next to files in a clean git working
   tree. A solution to add to needs `.dotskit/manifest.json` of the tool's version and a clean working tree.
   One command runs in a solution at a time: a second one, started while the first works, says so and changes
   nothing; the lock goes with the process that holds it, even a killed one.
2. Generates in a temporary folder what the template gives for the solution before the command (the base)
   and after it.
3. Compares the base, the new template output and the solution file by file, and shows the plan: every
   file with what happens to it and why, and the projects added to `All.sln` or removed from it.
4. Asks before writing. `--yes` writes without asking.
5. Writes all files or none: an error or Ctrl+C in the middle puts every file back. A process killed while
   writing leaves `*.dotskit-new` and `*.dotskit-old` files beside the solution's; `git status` shows them,
   and the next command refuses the unclean tree until they are cleaned up with git.
6. Builds `All.sln`.

What happens to a file:

| In the plan | When | What is written |
|---|---|---|
| `Add` | the template adds a file the solution does not have | the template's file |
| `Update` | the team did not change the file | the template's new file, with the line endings of the solution's |
| `Merge` | both changed it, in lines apart | both changes |
| `Delete` | the template removed a file the team did not change | the file is deleted |
| `Kept` | one side removed the file and the other changed it, or both changed a binary file | nothing: the solution's file stays |
| `Conflict` | both changed the same lines, or lines right next to each other | the file with both versions between `<<<<<<< solution` and `>>>>>>> template` |

`All.sln` is changed by `dotnet sln`, project by project, and never merged as text: `dotnet sln add` gives
a project a new GUID each time. Its projects are merged the way a file's lines are: a project the template added
joins it, one the template removed leaves it unless the team added it, and one the team took out stays out.

### Merged by git

In a solution there already, `dotskit new` and `dotskit upgrade` merge by git, the same three-way merge:

- the branch `dotskit/template` holds what the template gave the solution, a commit per version. git knows the
  base when the branch's last commit has the base's files and the working branch has that commit in its history;
  otherwise, as the first time or on another branch, the base is committed there and merged into the working
  branch with `git merge -s ours`: the solution's files stay as they are, and git knows the base from then on.
  `git merge --abort` leaves this commit, it changes no file. When a step of the command fails, the command takes
  back the merge, this commit and the move of the branch itself: git is as it was before the run;
- the command commits the template's new output there and runs `git merge --no-ff --no-commit dotskit/template`.
  git takes the template's changes and the team's, follows the files the template moved, and marks the lines
  both changed for the IDE or `git mergetool`. The merge stays uncommitted: look at it, commit it, or undo it
  with `git merge --abort`;
- the plan shows the template's own changes: a file it adds, changes or removes;
- what the solution's git ignores, by any of its `.gitignore` files, a local secret among them, and `All.sln`
  are never in the branch; the manifest and `All.sln` the command writes join the merge, so `git merge --abort`
  puts them back too;
- `--force` merges with `-X theirs`: a line both changed takes the template's; a file one side removed and the
  other changed stays a conflict;
- a file both changed is merged by a merge driver of dotskit, given to this one merge only: the base, the team's
  file and the template's are first brought to the template's style - no byte order mark, no spaces at the ends
  of lines, a line end at the end - so a change of the style alone conflicts with nothing the team wrote, and the
  merged file has the style. A solution file keeps its byte order mark, and the merged file keeps the line ends
  of the team's. `--merge files` does the same. A line break of two spaces at the end of a Markdown line is lost
  in a file both changed: write it as a backslash. A file with a merge of its own in `.gitattributes`, as one of
  Git LFS, keeps it.

What git then shows: `git log dotskit/template` - what each version of the template gave the solution;
`git diff dotskit/template HEAD`, once the merge is committed - the team's own changes over the template of its
version, and nothing else.

`--merge files` merges by the tool itself, as in the table above, and writes the files with no branch and no
merge commit. `--allow-dirty` takes `--merge files`: a git merge needs a clean tree.

## Manifest

`.dotskit/manifest.json` records what the solution was generated from: the template's version, the
arguments of the solution and the arguments of each service. Commit it with the solution: every later
command rebuilds the base from it.

The manifest keeps a port for each service. The template picks a random free port when `--HttpPort` is not
given, so the tool picks one once, the first port from 5000 that no service of the solution has, and a
service keeps its port when a later command adds a flag to it.

A flag with a part in `Common` or the root, such as `--Storage`, becomes the solution's when any of its
services has it. The solution's names, `--Database`, `--Deploy`, `--TestFramework`, `--Agent` and
`--GitHubCiCd` are set by the first command. A later command takes them from the manifest; a command that
gives one of them another value is refused.

## A solution made without dotskit

`dotskit new` works from the manifest. A solution made with the template alone, or before 2.8, has none, and
`dotskit init` writes it: it reads the solution, checks the reading and writes `.dotskit/manifest.json`, and
nothing else, after a yes.

What it reads:

- the version of the template from `DotNetSolutionKitVersion` in `Directory.Build.props`, there since 2.7.0;
- the names from `src/services/*.All.sln`; where a dot can belong to either name (`Acme.Corp.Shop`), from the
  image names of `deploy/build-images.sh`, or from `-N` and `-P` given to the command;
- the database, the deployment, the test framework and the agent by the files only one choice generates;
- the flags of `Common` and the root by the files each flag adds;
- each service under `src/services` with its own API project: its flags by the sections of its
  `appsettings.json` and the lines only a flag writes, its port by `launchSettings.json`.

The check: `init` generates the solution again by what it read, with that version of the template, and
compares it with the folder - "412 of 420 files of the template reproduced; these differ: ...". A file that
differs is a change of the team or a wrong reading; the report lists it so a person can tell which before
anything relies on the manifest. Files the team added are not counted, and `All.sln` is compared by its
projects. The check installs the template of that version from nuget.org.

| The folder | What `init` does |
|---|---|
| empty | writes nothing, and names `dotskit new -N <Company> -P <Product> -S <Service>` |
| no `src/services/*.All.sln` | writes nothing: not a solution of DotNetSolutionKit |
| a solution of 2.7 or later, no manifest | reads it, checks it, shows the manifest and the report, writes it after a yes |
| a solution before 2.7, from 1.0.0, which does not say its version | writes nothing, and asks for `--template-version`; the check then shows how well that version reproduces the solution, generated from nuget.org or, before 2.6.1, from its tag on GitHub |
| generated from the template's sources (version `source`) | writes nothing, and asks for the release it came from with `--template-version` |
| newer than the tool | writes nothing, and names `dotnet tool update -g SawKing.DotsKit.Tool` |
| of an earlier major version than the tool, other than the last release of the previous one | writes nothing, and names the tool of the solution's major |
| a service of another version than the solution | writes nothing, names the service and its version, and asks for the version to describe the solution by with `--template-version`; with it, the check shows what differs |
| with a manifest that lists every service | writes nothing: "the manifest is current: 2.8.0, services Orders, Billing" |
| with a manifest, and a service under `src/services` it does not list | adds the service, read from its files, after the same check |
| services, but no `Common` | writes the manifest without the check and says so: the database and the flags read from `Common` are the template's defaults; `dotskit new` takes `Common` for removed by the team and does not add it back |
| uncommitted changes in git | runs: it writes only `.dotskit/` |

## Upgrading a solution

```bash
dotnet tool update -g SawKing.DotsKit.Tool
dotskit upgrade
```

`dotskit upgrade` brings a solution to the tool's version of the template, keeping what the team changed. It
is the merge of `dotskit new`: the base is the solution's manifest generated by the solution's version, the
target the same manifest generated by the tool's. It needs a manifest - a solution made without `dotskit`
gets one from `dotskit init` - and a clean working tree.

What the merge adds for an upgrade:

- a file the template moved (`git diff -M` between the two outputs) takes the team's changes to its new
  path, and the old path is deleted;
- a file the template removed is deleted when the team did not change it, and kept and reported when it did;
- the package versions follow the manifest's `packages`, below;
- after writing, the solution is built; the references and `using` lines of the types the template moved are
  repaired, below, and the errors left are listed by project.

A type the template moves to another project or namespace, as 3.0 moves the types of `Common`, breaks the
team's code that uses it. The tool reads where each public type lives - project and namespace - from both
outputs of the template it generated for the merge, so the step works for any version and needs no list kept
by hand. A type is followed inside its own area: a service's copy of a type leads to the same service, a type of
the shared code to the shared code, so a service never gets a `using` or a reference of another service.

Right after the merge is written, before any build and whether it has conflicts or not, every `using` of the
solution's code that names a namespace the template no longer has is replaced by where its types went, and the
project gets the reference to their projects. A file left in conflict, a file of a project left in conflict and a
namespace the solution's own code still declares are left for the build after the conflicts are resolved. In the
merge by git the changed files join the merge. Then the build has less to find, often nothing. After a build that
fails:

- a `using` of a namespace the template no longer has (`CS0234`) becomes the `using` of the namespaces its
  types went to, and the project gets a reference to the projects they are in;
- a type not found (`CS0246`) that the template moved out of a namespace the file uses gets its `using` and
  the reference to its project; a type of the team's with a template's name, a type the template did not move,
  or moved to two homes, is left as it is;
- the solution is built again, while each round finds something new to repair: once a project builds, the
  projects that reference it show their own errors.

The report says how many references and `using` lines changed; `git diff` shows them. The repair runs after
the merge is written: a write of it that fails stops it, and the report says why. With `--no-build` there is
no build and no repair.

Which versions: one major version at a time. dotskit X.y upgrades a solution of every minor of X and of the
last minor of X-1. From an older major the tool names the steps: the tool of the solution's own major,
`dotskit upgrade`, then the next major, and so on. A solution newer than the tool is refused with the command
that updates the tool. The tools of 2.x also take 1.0.0, the only release of 1.x. The tool generates a
version from its package on nuget.org, which has the template from 2.6.1, and an earlier release from the
archive of its tag on GitHub, so a solution of any release from 1.0.0 is upgraded the same way.

`--to 2.x.y` brings the solution to that release instead of the tool's version, a release newer than the
solution and not newer than the tool. A solution far behind can go a release at a time, each step a merge of
its own with a build after it:

```bash
dotskit upgrade --to 2.0.0
dotskit upgrade --to 2.1.0
dotskit upgrade
```

Package versions come with an upgrade as far as the manifest lets them:

```json
"packages": {
  "updates": "minor",
  "pinned": { "Npgsql": "8.0.4" }
}
```

| `updates` | What an upgrade brings |
|---|---|
| `minor` (default) | patches and minor versions |
| `patch` | patches only |
| `none` | no change of a package the solution has |

A package in `pinned` stays on that version. A major version of a package comes only with a major version
of the template. A package new to the solution comes as the template has it. Before the merge, the held
versions are written into the template's side, so these lines do not conflict - a version the team raised
itself does, unless it is pinned; the plan has a line for each
package that changes or is held - "Npgsql: pinned on 8.0.4, the template brings 8.0.11". When the
template's new code needs a newer version than a pinned one, the build fails and the report names the
project.

The upgrade does the mechanical part: the text of the files, moves and removals, the package versions, the
build. Whether the team's own code should change with the template's - a handler of its own beside one the
template changed - is the team's to decide, and its tests show it.

## Options

| Option | What it does |
|---|---|
| `--yes` | writes without asking, for CI |
| `--force` | resolves a conflict with the template's lines; the team's lines stay in git history |
| `--no-build` | does not build the solution after writing |
| `--allow-dirty` | runs on a working tree with uncommitted changes |
| `--template-version 2.x.y` | `init`: the version the solution was made by, where it does not say it |
| `--to 2.x.y` | `upgrade`: the release to bring the solution to, not newer than the tool |
| `--merge files` | `new` and `upgrade`: merge by the tool and write the files, instead of a git merge of `dotskit/template` |

`DOTSKIT_TRACE=1` prints every `dotnet` and `git` call of a command with its time to stderr: what a slow
command spends its time on.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | written, and the solution builds; with `--no-build`, written |
| `1` | refused or failed, with the reason on one line; nothing was written because the answer was not yes; or written, and the build fails or was cancelled |
| `2` | written with conflicts: resolve them, then build |

## Limits of this version

- `dotskit new` works in a solution with a manifest: made by `dotskit`, or described by `dotskit init`.
- `dotskit new` needs a manifest of the tool's version: a solution of an earlier version is brought to it by
  `dotskit upgrade` first.
