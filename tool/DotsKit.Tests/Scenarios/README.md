# Scenarios of dotskit

A scenario runs the tool the way a team does: a real solution on disk, the real `git`, `dotnet new` and
`dotnet build`. The unit tests beside this folder decide the parts; a scenario shows the parts together. Every
scenario is in the category `Sandbox.Category`, and each file's summary links back to this page.

| File | What it checks |
|---|---|
| `Sandbox.cs` | Not a scenario: a solution generated once from the template's sources and copied per test, the tool run as the command line a person runs, `git` around it. |
| `TeamScenarios.cs` | A solution several people work on: a project added by hand, a service left out of `All.sln`, a service named twice, a local secret, a new service's secrets file; with the template alone and with dotskit. |
| `InitScenarios.cs` | A solution made without dotskit: `init` describes it, the template generates it again by that description, and dotskit adds to it. |
| `UpgradeScenarios.cs` | A solution of an earlier release brought to the template's sources by `upgrade`, the team's changes and package pins kept. |
| `ChainScenarios.cs` | A solution of 1.0.0 with the team's code on `Common`, upgraded a release at a time to the last minor of each 2.x and to the sources; every step builds. Needs the network for the tags before 2.6.1. |
| `MovedTypeScenarios.cs` | A type of the template moved to another project and namespace, as 3.0 moves `Common`'s; the team's code that used it builds again by the repair of `upgrade`. |
| `GitMerge/` | The merge by git through the branch `dotskit/template`, below. |

## GitMerge

The merge by git on its own, with `GitTemplateMerge` and a solution of `GitSolution`: no template runs, the
template's outputs are folders a test writes.

| File | What it checks |
|---|---|
| `GitSolution.cs` | Not a scenario: the solution in git as the template 1.0 gave it, the template's outputs, the texts the scenarios compare. |
| `StyleScenarios.cs` | The three sides of a file brought to the template's style: a change of style alone is no conflict, a solution file keeps its byte order mark, the merged file keeps the team's line ends. |
| `MergeScenarios.cs` | The template's change merged with the team's; a line both changed is a conflict for the IDE; what the solution's git ignores and `All.sln` are never in the branch; the base adopted again on a branch without it. |
| `RollbackScenarios.cs` | A run that cannot go on is taken back by `git reset --hard` to the commit it started from: a merge over one in progress is refused; a step that fails after the merge, a tracked file a later step changed, a staging that fails over a manifest in git and a merge git refuses leave HEAD, the branch and the files as before the run; an undo git refuses names the lock and the commands. |
| `StagingScenarios.cs` | What the command stages beside the merge reaches git whole, however many files: the paths go in a file, not on the command line. |
