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
4. Asks before writing. `--yes` writes without asking. A tree that was clean at the start is written only while it
   is clean at the same commit; under `--allow-dirty`, only while its files are as the command found them. A file
   saved or a commit made while the plan is built or waits for a yes stops the write, and nothing is written.
5. Writes the files. Each new content goes beside its file first, then the files are swapped in with the
   originals kept aside. An error or Ctrl+C before the write is done puts the originals back; a file the disk
   refuses to put back is named in the error, with its original beside it as `*.dotskit-old`, and a new file it
   refuses to remove is named to delete. In the merge by git, git then takes back the rest of the run,
   [below](#merged-by-git). After a write that is done, an original the disk refuses to remove, as one another
   process holds, stays beside its file. A process killed while writing leaves `*.dotskit-new` and
   `*.dotskit-old` files beside the solution's. `git status` shows what is left, and the next command refuses
   the unclean tree until it is cleaned up with git.
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

A file of the solution its git ignores, a local `appsettings.Secrets.json` among them, is never written, in either
merge. A new file the template adds that the solution's git would ignore, as the secrets file of a new service, is
written by both: the merge by git, which leaves it off the branch, writes it beside the merge, and `git merge --abort`
by hand leaves it, as git does not know it.

`All.sln` is changed by `dotnet sln`, project by project, and never merged as text: `dotnet sln add` gives
a project a new GUID each time. Its projects are merged the way a file's lines are: a project the template added
joins it, one the template removed leaves it unless the team added it, and one the team took out stays out.

### Merged by git

In a solution there already, at the root of its git repository, `dotskit new` and `dotskit upgrade` merge by
git, the same three-way merge:

- the branch `dotskit/template` holds what the template gave the solution, a commit per version. git knows the
  base when the branch's last commit has the base's files and the working branch has that commit in its history;
  otherwise, as the first time or on another branch, the base is committed there and adopted by the working
  branch the way `git merge -s ours` would: a merge commit of the branch's own tree with the base as its second
  parent, made by `git commit-tree` and `git update-ref`. The solution's files stay as they are, and git knows the
  base from then on. No commit hook of the team runs in a run (pre-commit, prepare-commit-msg, commit-msg,
  pre-merge-commit, post-merge); hooks of refs and the index, as reference-transaction, run as git runs them. The
  commits dotskit makes are not signed.
  `git merge --abort` leaves this commit, it changes no file. The merge does not start over a tree changed after
  the command started, as a file saved while the plan waited for a yes. When a later step fails, the command runs
  `git reset --hard` to the commit it started from and puts the branch back: this takes back the merge, this
  commit and every tracked file the run changed, and leaves the files git does not track. A tracked file changed
  by hand while the merge runs is taken back with them. When git refuses, as over an `index.lock` left by a git
  that was stopped, the error names the commands that do it by hand;
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

### What dotskit runs in git

Every git command the tool runs, by what it does to the solution's repository. A list of paths goes to git in
a file (`--pathspec-from-file`), never on the command line, so a solution of any size fits.

| Does to the repository | Command | What for |
|---|---|---|
| nothing, reads | `git status --porcelain` | a clean working tree before writing |
| | `git rev-parse --show-prefix` | whether the solution is at the root of its repository, for the merge by git |
| | `git ls-files -v` | files marked `assume-unchanged`: git hides their local changes from `git status`, and a rollback would lose them, so the command is refused |
| | `git rev-parse` (`HEAD`, `--absolute-git-dir`, `MERGE_HEAD`, the branch, a commit's tree) | where the solution and the branch stand |
| | `git merge-base --is-ancestor` | whether the working branch has the branch's last commit |
| | `git config --path --get core.attributesFile` | the user's global attributes, kept under the merge driver's |
| | `git ls-files --others --ignored --exclude-standard` | the files the solution's git ignores, never written |
| | `git diff --name-only --diff-filter=U` | the files left in conflict |
| | `git diff --no-index -M` | the files the template moved, between two of its outputs outside the repository |
| | `git merge-file -p`, again with `--theirs` under `--force` | a file merged by `--merge files` and by the merge driver, on copies of the three sides; the first run counts the conflicts `--force` decides |
| | `git ls-remote --tags` | the releases of the template, for `init` on a solution that does not say its version |
| writes objects only, through an index of its own | `git add -A`, `git rm --cached`, `git ls-files`, `git write-tree`, `git commit-tree` | a commit of the template's output; the solution's index and files are not touched |
| moves the branch `dotskit/template` | `git update-ref refs/heads/dotskit/template` | the commit of the template's new output on the branch, only from the tip read before |
| commits on the working branch, files unchanged | `git commit-tree -p HEAD -p <base>`, `git update-ref HEAD` | the base adopted, the first time or on another branch, with no commit hook of the team |
| merges into the working tree, uncommitted | `git -c core.attributesFile=… -c merge.dotskit.driver=… merge --no-ff --no-commit dotskit/template`, with `-X theirs` under `--force` | the template's change merged with the team's, by the merge driver `dotskit merge-driver` given to this one merge |
| stages | `git add` | the manifest, `All.sln` and the files the repair changed join the merge |
| undoes a run that failed | `git reset --hard`, `git update-ref` (or `-d`); `git rev-parse --git-path index.lock` when git refuses | the working branch, its tracked files and the branch `dotskit/template` back where they were before the run |

The tool never commits the merge, never pushes, and never checks out a branch: the merge is the team's to look
at, commit or undo.

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
| a solution before 2.7, from 1.0.0, which does not say its version | with `Common`, checks it against the last patch of each minor before 2.7, side by side, from the tags of the repository, shows the share of the template's files each reproduces, writes nothing, and names the closest: `dotskit init --template-version <it>`, or, when it is 1.0.0, `2.0.0`, to bring the code to by hand first ([upgrading](upgrading.md#a-solution-of-v1)); without `Common`, asks for the version; with the version given, the check shows how well it reproduces the solution, generated from nuget.org or, before 2.6.1, from its tag on GitHub |
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
gets one from `dotskit init` - and a clean working tree. Its first lines name the minor versions it crosses and
link [upgrading.md](upgrading.md) on GitHub, of master for a release and of dev for a pre-release: its sections
for these versions say what a solution may have to change by hand.

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

Right after the merge is written, a project of the template references the projects of the template its new
version references, no more and no fewer: a reference its earlier version got wrong goes, one it adds comes, and a
reference to a project of the team's stays; on central package management, a version a `PackageReference` still
names goes to `src/Directory.Packages.props` - one there already stays - and leaves the project. A project left
in conflict waits for its resolution. Then, before any
build and whether it has conflicts or not, every `using` of the
solution's code that names a namespace the template no longer has is replaced by where its types went, and the
project gets the reference to their projects. A type the template moved to another namespace that the code names
by its full name - as a type, in `new`, `typeof`, a generic argument or before a static member, with `global::` or
without, in an alias or a static using - gets its new namespace in place, read from the C# syntax, and the project
the reference; a `cref` of an XML comment and code under an inactive `#if` are left to the build. A file left in conflict, a file of a project left in conflict and a
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

The report names, by project, the `using` lines and full names it changed and the references it added; `git diff` shows them. The repair runs after
the merge is written. The one before the build is a step of the write: in the merge by git a failure or Ctrl+C
there takes back the run; in the merge by files the files stay written, and the error says so. A write of the
repair after a build that fails stops the repair, and the report says why. With `--no-build` there is
no build and no repair.

Which versions: one major version at a time. dotskit X.y upgrades a solution of every minor of X and of the
last minor of X-1. From an older major the tool names the steps: the tool of the solution's own major,
`dotskit upgrade`, then the next major, and so on. A solution newer than the tool is refused with the command
that updates the tool. The tool generates a version from its package on nuget.org, which has the template from
2.6.1, and an earlier release from the archive of its tag on GitHub, so a solution of any 2.x release is
upgraded the same way. The tools of 2.x also take 1.0.0, the only release of 1.x, but it differs from 2.0.0 in
most files: a solution of v1 is brought to 2.0.0 by hand first ([upgrading](upgrading.md#a-solution-of-v1)).

`--to 2.x.y` brings the solution to that release instead of the tool's version, a release newer than the
solution and not newer than the tool. A solution far behind goes to the tool's version at once or through
releases between, as the team decides: either way the upgrade goes a part at a time, so a step is a part, not
the whole distance. A release between is worth a stop where the team wants to build and test the solution on it:

```bash
dotskit upgrade --to 2.7.0   # the parts of 2.7.0; build and test
dotskit upgrade              # the parts of the tool's version
```

An upgrade goes one part a run, so the team resolves the conflicts of one part before the next: first the
shared parts - the root with `deploy` and the scripts, then the files of `src/common` and each of its projects, a
project before those that reference it, then `src/capabilities` - then a service folder at a time, in order. A
project the template adds to `src/common` is a part of its own, after those it references; the first run takes
the parts and the manifest keeps them. A package the template drops keeps its version until no project of the
parts left references it. A folder of the team's own is a part too, which merges nothing. Each run is the same merge, of the template's new files
for the parts merged so far and this one over its files for the parts before. After the merge the run repairs the
usings and project references of the types the template moved between the parts merged so far, in every project
of the solution, and names them by project; a type that goes to a part not yet merged waits for it, and the usings
of its namespace stay. Then the solution builds, and its errors repair the team's files, leaving the template's
files of a part not merged yet to that part; a service whose files of the template wait for their part may
fail until it, and the build names it. The manifest keeps the release and the parts merged, and the next
`dotskit upgrade` takes the next part; `--part Orders` names one, after the shared parts. A part is committed
before the next; the last one moves the manifest to the release, and `dotskit new` waits until then. When the
last part leaves conflicts, the upgrade stays open: once they are resolved and committed, `dotskit upgrade`
changes no file of the template, repairs the usings of the whole upgrade, builds and moves the manifest.
`--all-at-once` merges every part in one run. The template's output for a version is generated once and kept
in the temp folder (`dotskit/outputs`) for two days, so a part after the first takes seconds, not the generation.

```bash
dotskit upgrade --to 2.7.0               # the shared part; resolve, commit
dotskit upgrade                          # the next part; resolve, commit
dotskit upgrade --part Billing           # the service named
dotskit upgrade --to 2.7.0 --all-at-once # or every part in one merge
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
| `--force` | resolves a conflict with the template's lines; with `--merge files` the plan names each file it did that in; the team's lines stay in git history, so it needs a clean tree and refuses `--allow-dirty` |
| `--no-build` | does not build the solution after writing |
| `--allow-dirty` | runs on a working tree with uncommitted changes |
| `--template-version 2.x.y` | `init`: the version the solution was made by, where it does not say it |
| `--to 2.x.y` | `upgrade`: the release to bring the solution to, not newer than the tool |
| `--all-at-once` | `upgrade`: every part in one merge, instead of the shared files first, then a service folder at a time |
| `--part <part>` | `upgrade`: the part this run takes, `shared`, `common/<project>`, `capabilities` or a service folder's last name |
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
- The merge by git needs the solution at the root of its git repository. A solution in a folder of a larger
  repository is refused it before the plan and takes `--merge files`. The clean working tree is checked over
  the whole repository.
