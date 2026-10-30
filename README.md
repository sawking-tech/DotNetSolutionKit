# DotNetSolutionKit (dotskit)

[![NuGet](https://img.shields.io/nuget/v/SawKing.DotNetSolutionKit?label=nuget&color=1f9d4c)](https://www.nuget.org/packages/SawKing.DotNetSolutionKit)

Made by Vladimir Savkin at [sawking.tech](https://sawking.tech/).

Site: [dotskit.sawking.tech](https://dotskit.sawking.tech/). Documentation, in English and Russian:
[dotskit.sawking.tech/docs.html](https://dotskit.sawking.tech/docs.html).

**DotNetSolutionKit (dotskit)** generates a .NET solution to a team's needs - the frame for DDD and Clean Architecture, CI, deployment and tests, with modules and integrations with infrastructure services turned on by flags. It carries no business logic of its own: a monolith, one web service or a high-load set of microservices is written on top, as strictly as the team chooses. `dotskit` adds services to the solution and flags to its services. The generated solution is an ordinary .NET solution the team owns.

`dotskit upgrade` brings the template's later improvements into a generated solution under the team's control and helps the team maintain its solution: a three-way merge with the team's code, where what does not merge is a conflict for the team to resolve.

No lock-in, and nothing imposed. A generated solution is an ordinary .NET solution: no runtime of the
template, no package of ours it has to reference, no service to call home. The tool is useful, not required -
the template works through `dotnet new` alone, and a solution that stops upgrading keeps building and shipping.
The same goes for the architecture: DDD and Clean Architecture are what the layout is ready for, not rules it
enforces. A team develops the way it finds right, carries a minimum of files it did not ask for, and can try
what the template offers whenever it wants to.

What it gives a solution in full - the principles, the economics, the cost:
[what the template gives](docs/getting-started/what-it-gives.md).

## Install and generate

The recommended way is the tool [dotskit](docs/getting-started/tool.md): it generates a solution, adds
services and flags to it, and upgrades it to a newer version of the template, keeping the team's changes by
a three-way merge. A solution it generates carries its manifest, so `dotskit upgrade` works from the first
commit.

```bash
dotnet tool install -g SawKing.DotsKit.Tool

dotskit new -N MyCompany -P MyProduct -S Orders   # in an empty folder: the solution and its first service
dotskit new -S Billing --Storage true             # in the solution: one more service
dotskit new -S Orders --MongoDB true              # in the solution: a flag for a service it has
dotskit init                                      # in a solution made without dotskit: describe it in its manifest
dotskit upgrade                                   # in the solution: bring it to the tool's version of the template
dotskit upgrade --to 2.3.0                        # in the solution: to an earlier release, a step at a time
```

A solution of an earlier 2.x version is brought to the current one by `dotskit upgrade`: git keeps the team's
changes where they merge with the template's, and the rest are conflicts to resolve. A solution of v1 is brought
to 2.0.0 by hand first ([upgrading](docs/getting-started/upgrading.md)).

<details>
<summary>The template alone, by <code>dotnet new</code></summary>

```bash
dotnet new install SawKing.DotNetSolutionKit

# the shared Common projects, the solution file and the first service
dotnet new DotNetSolutionKit -N MyCompany -P MyProduct -S Orders --Solution

# every further service; dotskit is the template's second short name
dotnet new dotskit -N MyCompany -P MyProduct -S Billing
```

A solution generated this way is upgraded by `dotskit` after `dotskit init`.
To try the template from a clone: `dotnet new install /path/to/DotNetSolutionKit/template`.

</details>

The packages are [SawKing.DotsKit.Tool](https://www.nuget.org/packages/SawKing.DotsKit.Tool) and
[SawKing.DotNetSolutionKit](https://www.nuget.org/packages/SawKing.DotNetSolutionKit) on nuget.org; every
[release](https://github.com/sawking-tech/DotNetSolutionKit/releases) also carries them as `.nupkg` files.

What the generated solutions look like:
[DotNetSolutionKit.Samples](https://github.com/sawking-tech/DotNetSolutionKit.Samples), a branch each for a
single service, a gateway with services and the full kit, regenerated from every release with their CI
running, and `full-dev`, generated from `dev`, where the next release is built. The [samples page](https://dotskit.sawking.tech/samples.html)
shows their files and how their CI ran.

| Parameter | Default | What it does |
|---|---|---|
| `-N`, `--NamespaceRoot` | `MyCompany` | Organization name, the root namespace. May be dotted. |
| `-P`, `--ProductName` | `Product` | Product name. May be dotted. |
| `-S`, `--ServiceNameOrCustom` | `Service` | Service name. May be dotted. |
| `--Solution` | `false` | Generates the solution: `Common`, `All.sln` and the root files, with the first service. Without it, only the service folder, into a solution already there. |
| `--Database` | `postgres` | [Database](docs/architecture/persistence.md#sql-server): `postgres` or `mssql` (SQL Server). |
| `-H`, `--Hangfire` | `true` | [Background jobs](docs/features/background-jobs.md) on Hangfire. |
| `--Messaging` | `none` | [Message bus](docs/features/messaging.md): `outbox` or `direct`. |
| `-I`, `--Infisical` | `false` | [Secrets and settings from Infisical](docs/features/secrets.md). |
| `--Vault` | `false` | [Secrets and settings from HashiCorp Vault](docs/features/secrets.md#hashicorp-vault). |
| `--DiffApi` | `false` | [API contract diff](docs/features/api-diff.md) on pull requests. |
| `--GitHubCiCd` | `false` | [CI on GitHub Actions](docs/features/ci.md): build, tests on real servers, coverage on demand, secret scan. |
| `-FF`, `--FeatureFlags` | `false` | [Feature flags](docs/features/feature-flags.md). |
| `--HierarchyRules` | `false` | [Access rules over a tenant tree](docs/features/hierarchy-rules.md). |
| `--Storage` | `false` | [Object storage](docs/features/object-storage.md), S3-compatible. |
| `-CH`, `--ClickHouse` | `false` | [ClickHouse](docs/features/clickhouse.md): connections, schema check, readiness. |
| `--MongoDB` | `false` | [MongoDB](docs/features/mongodb.md) beside the main database: the client, the service's database, readiness. |
| `--Notify` | none | Channels of [notifications](docs/features/notifications.md): `email` - through SMTP or Graph API, with a sandbox outside Production; with `--Messaging` other services ask for it by a bus command. |
| `--TestFramework` | `nunit` | Test framework of the service's tests: `nunit` or `xunit` (v3). |
| `--Audit` | `false` | [Audit journal](docs/features/audit.md) of entity changes, through the outbox; with `--Messaging outbox`. |
| `--ApiGateway` | `false` | An [API gateway](docs/features/api-gateway.md) on YARP in place of a service, into a solution already there (without `--Solution`). |
| `--Deploy` | `compose` | [Deployment files](docs/operations/deployment.md): `compose`, `k8s` or `none`. |
| `--Agent` | `claude` | [Rules and skills for an AI agent](docs/getting-started/agents.md): `claude`, `opencode` or `none`. |
| `--HttpPort` | free port | Port in `launchSettings.json` and on the host under compose. |

Details, dotted names and running locally:
[generating a solution](docs/getting-started/template.md).

## Documentation

Everything else is in [docs](docs/README.md): the architecture, each feature, operations, the decisions
behind the parts that depart from common practice, and the roadmap. The
[site](https://dotskit.sawking.tech/docs.html) shows the same documents with a menu and rendered diagrams, in
English and Russian.

What each version brings: [version.json](version.json) and the
[releases](https://github.com/sawking-tech/DotNetSolutionKit/releases). How versions are made:
[contributing](CONTRIBUTING.md).

## License

[![MIT License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
