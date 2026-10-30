# dotskit

**dotskit** is two things under one name: the `dotnet new` template that generates a .NET microservice
solution, and the command-line tool that generates it, adds to it and upgrades it. A generated solution is an
ordinary .NET solution the team owns; the template and the tool are how it starts and how it keeps up with the
template.

```bash
dotnet tool install -g SawKing.DotsKit.Tool

dotskit new -N MyCompany -P MyProduct -S Orders   # in an empty folder: the solution and its first service
dotskit new -S Billing --Storage true             # in the solution: one more service
dotskit upgrade                                   # in the solution: bring it to the tool's version of the template
```

## The pages

| Page | Its subject |
|---|---|
| [What the template gives](what-it-gives.md) | the architecture, the infrastructure and the checks a solution gets, and what they save |
| [Generating a solution](template.md) | the template: what it generates, its parameters and flags, `dotnet new` |
| [The layout of a solution](layout.md) | the folders of a generated solution, the projects of one service, and how the template and the tool meet |
| [The dotskit tool](tool.md) | the tool: `new`, `init`, the manifest, the git commands, the options and the exit codes |
| [Upgrading a solution](upgrading.md) | an upgrade from end to end: its course, its repairs, its speed, and what each release asks of a solution by hand |
| [Working with AI agents](agents.md) | the rules and skills a generated solution gives an agent |

The rest of the documentation: [the pages at a glance](../README.md) for architecture and operations, the
[decisions](../adr/) behind them, and [the roadmap](../roadmap.md).

## The way in

- A new solution: [generating a solution](template.md), then the tool for every service after the first.
- A solution made with the template alone: `dotskit init` writes its manifest ([the tool](tool.md#a-solution-made-without-dotskit)), and `dotskit upgrade` works from there.
- A solution behind the current release: [upgrading a solution](upgrading.md).
- A team weighing whether to take this on: [what the template gives](what-it-gives.md).
