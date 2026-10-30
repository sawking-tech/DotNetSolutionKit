# dotskit

`dotskit` is the command-line tool of [DotNetSolutionKit](https://www.nuget.org/packages/SawKing.DotNetSolutionKit),
the `dotnet new` template of .NET microservice solutions. It makes a solution with the template, adds services
to it and flags to its services, and upgrades it to a newer version of the template. Where the team and the
template changed the same file, it merges the two the way git merges branches.

**Documentation, in English and Russian: [dotskit.sawking.tech](https://dotskit.sawking.tech/docs.html#dotskit)**

## Install

```bash
dotnet tool install -g SawKing.DotsKit.Tool
```

dotskit X.Y.Z generates with the template X.Y.Z, which it installs from NuGet into a hive of its own;
the template installed with `dotnet new install` is left as it is.

## Commands

```bash
# in an empty folder: a solution with its first service
dotskit new -N MyCompany -P MyProduct -S Orders

# in the solution: one more service, with the solution's names, database and deployment
dotskit new -S Billing --Storage true

# in the solution: a flag for a service it has
dotskit new -S Orders --MongoDB true

# in a solution made with the template alone: describe it in its manifest
dotskit init

# in a solution: bring it to the tool's version of the template
dotskit upgrade

# in a solution far behind: stop at a release between, a part a run
dotskit upgrade --to 2.3.0
```

Every command shows the files it will change and why, and asks before writing; `--yes` writes without asking.
An upgrade goes a part a run - the shared files first, then a service at a time - and each run merges by git,
repairs the usings and references the new version asks for, builds the solution and leaves the merge for the
team to commit. A conflict stays in the files for the IDE or `git mergetool`, and the command exits with 2;
a run that fails is taken back by git. `--merge files` writes the files directly instead, all or none, with
conflicts marked `<<<<<<< solution` / `>>>>>>> template`.

What a solution was made from is kept in `.dotskit/manifest.json`: commit it with the solution.

Every git command the tool runs in a solution, and what each does to the repository, is listed in
[dotskit.md, What dotskit runs in git](https://github.com/sawking-tech/DotNetSolutionKit/blob/master/docs/getting-started/dotskit.md#what-dotskit-runs-in-git).
It never commits the merge, never pushes and never checks out a branch.

What a solution may have to change by hand, release by release, is in
[upgrading.md](https://github.com/sawking-tech/DotNetSolutionKit/blob/master/docs/getting-started/upgrading.md);
`dotskit upgrade` names its sections for the versions it crosses.

The template works without the tool; this package is the way to keep a solution up with it.
