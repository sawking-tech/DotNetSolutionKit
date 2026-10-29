# Roadmap

Releases done, the step in progress and the planned steps, in order. The axis counts steps, not dates; a longer bar is a larger step, and bars side by side can go in parallel.

```mermaid
gantt
    dateFormat X
    axisFormat %s
    tickInterval 1second

    section Done
    v1.0 the template before 2026                         :done, s1, 0, 3000ms
    v2.0 layered services, shared host, flags             :done, s2, after s1, 3000ms
    v2.1 deployment, API gateway, CI, correlation         :done, s3, after s2, 2000ms
    v2.2 idempotent commands                              :done, s4, after s3, 500ms
    v2.3 surviving a secret store outage                  :done, s5, after s4, 500ms
    v2.4 tokens in cookies, CSRF                          :done, s6, after s5, 500ms
    v2.5 audit journal                                    :done, s7, after s6, 1000ms
    v2.6 NUnit or xUnit                                   :done, s8, after s7, 1000ms
    v2.7 site, samples, nuget.org, major check, gateway (#5), Vault, settings reloaded, SQL Server :done, s9, after s8, 4000ms
    v2.8 dotskit new, init and upgrade; a service adds itself to All.sln; MongoDB, email, read-only containers, coverage on demand :done, s10, after s9, 4000ms

    section Now
    2.9 - dotskit upgrade adds the project references and using lines of types the template moved (#117) :active, s32, after s10, 1500ms
    template.yml starts every generated service and waits for it to be healthy (#107) :s33, after s10, 500ms
    bluegreen.sh says what is missing when a solution has no service to deploy (#103) :s34, after s33, 300ms
    a table of flags - what a flag requires, excludes or gives with another (#113) :s35, after s34, 500ms
    Samples - a branch that lives a product's life, upgraded by dotskit (#104) :s36, after s10, 1500ms

    section Next
    3.0 on .NET 8 - shared code by kind, the configuration store, flags in lower case (#101, #105, #106) :s15, after s32, 3000ms
    4.0 on .NET 10; 3.x stays on .NET 8 until 10 November 2026 :s31, after s15, 3000ms

    section Ideas
    push to connected clients through SignalR (#71) :s37, after s15, 1000ms
    document presentation, and templates on top of it (#74) :s38, after s37, 2000ms
    a generated solution releases the way the template does (#64, #66) :s39, after s38, 1500ms
```

What each release brought: [version.json](https://github.com/sawking-tech/DotNetSolutionKit/blob/master/version.json).
Work goes into `dev`, and a release reaches `master` about once a week ([CONTRIBUTING](https://github.com/sawking-tech/DotNetSolutionKit/blob/master/CONTRIBUTING.md)).
Since 2.8 `dotskit` keeps a solution up with the template for as long as the solution lives: `dotskit new`
adds services and flags to it, `dotskit init` describes a solution made without the tool, and `dotskit upgrade`
brings it to a newer version, merging the template's changes with the team's. Everything in Now goes into
2.9, on .NET 8. 3.0 stays on .NET 8 and splits `Common` into a project per capability, so a service carries only the
packages it uses; a 2.x solution is upgraded to it by `dotskit`, which since 2.9 adds the project references and `using` lines of the types the template moved. 4.0
moves to .NET 10. Support for .NET 8 ends on 10 November 2026: 3.0 and 4.0 are out before it, and 3.x then
takes fixes only. Ideas have an issue each and no version yet.

| Version | What changes for a solution |
|---|---|
| 2.8 | `dotskit` keeps a solution up with the template: `dotskit new` adds a service or a flag and keeps the team's changes, `dotskit init` describes a solution made without it, `dotskit upgrade` brings a solution to its version; a service added with the template alone puts itself into `All.sln`, without a script; MongoDB and email by flag, each a project of its own in `src/capabilities`; containers on a read-only filesystem; the test servers start with one command, the same locally as in CI; coverage on demand; `--Solution` in place of `-M false`; the template answers to the short name `dotskit` too (`dotnet new dotskit`) |
| 2.9 | `dotskit upgrade` builds the solution after the merge and adds the project references and `using` lines a moved type needs, from an index of where each type of the template lives; what is left is listed by project; a solution of any release from 1.0.0 is upgraded, the releases before nuget.org taken from their tags, and `--to` goes a release at a time (#117); `dotskit new` and `dotskit upgrade` merge by git through the branch `dotskit/template`, a conflict resolved in the IDE, and `--merge files` keeps the tool's own merge; `bluegreen.sh` says what is missing when there is no service to deploy (#103); the docs list what each flag requires, excludes or gives with another (#113); the template's files are UTF-8 with LF line ends, a line end at the end and no spaces at the ends of lines, a byte order mark only in a solution file; `dotskit upgrade` brings that to a solution, its merge bringing the three sides of a file to the style first, so the style alone conflicts with nothing the team wrote; a project the team took out of `All.sln` stays out, and an upgrade by git that fails leaves no commit and no branch behind |
| 3.0 | The shared code is laid out by what it is. `src/framework` - the systems the template brings as a way of working, always there and used as they are: the three-phase domain events ([ADR-001](adr/001-three-phase-domain-events.md)) and the testing system ([ADR-005](adr/005-testing-a-service.md)). `src/capabilities` - what a flag turns on, a project per flag, configured and not changed: feature flags, MongoDB, email, ClickHouse, object storage, the audit journal; a flag that is off leaves its project out whole. `src/common` - what the services of the product share and the team changes to its needs: the contracts, the database context base, the web pipeline. A namespace names its kind (`NamespaceRoot.ProductName.Capabilities.Mongo`). The secret store becomes the configuration store. `-M` is gone (`--Solution` since 2.8), flags are written in lower case (`--api-gateway`), the package is `SawKing.DotsKit.Templates`. `dotskit upgrade` takes a solution of the last 2.x there, and its repair of 2.9 adds the references and `using` lines of the types that moved |
| 4.0 | .NET 10 |

## Reducing lock-in

The defaults are the author's preferences; a team with its own standard should be able to replace them.

| Technology | Where the template depends on it now |
|---|---|
| PostgreSQL | none: `--Database mssql` generates the solution on SQL Server, each of these behind the same seam, see [persistence](architecture/persistence.md#sql-server) |
| Infisical | none: `--Vault` reads the same way from HashiCorp Vault, through the same `ISecretStore` port, see [secrets](features/secrets.md#hashicorp-vault) |
| GitHub CI | only the generated workflow; the checks are scripts any CI can call |
