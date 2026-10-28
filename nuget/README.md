# DotNetSolutionKit (dotskit)

**DotNetSolutionKit (dotskit)** is a configurable `dotnet new` template and lifecycle toolkit for standardized .NET solutions.

It provides ready-to-use architectural building blocks, modules, and integrations with infrastructure services that can be combined through flags to create different solution variants tailored to a team's needs and infrastructure. It generates microservice solutions based on DDD and Clean Architecture, with CI, deployment, and testing, adds services, connects modules to them, and upgrades solutions to new template versions while preserving the team's code. The generated solution remains an ordinary .NET project owned by the team, allowing them to focus on the architecture of the solution they are building and its business logic.

**Site and documentation, in English and Russian: [dotskit.sawking.tech](https://dotskit.sawking.tech/)**

## Install

```bash
dotnet new install SawKing.DotNetSolutionKit
```

## Generate

```bash
# the shared Common projects, the solution file and the first service
dotnet new DotNetSolutionKit -N MyCompany -P MyProduct -S Orders --Solution

# every further service; dotskit is the template's second short name
dotnet new dotskit -N MyCompany -P MyProduct -S Billing
```

Generated solutions, regenerated from every release with their CI running:
[DotNetSolutionKit.Samples](https://dotskit.sawking.tech/samples.html).

## dotskit

The template's command-line tool, [SawKing.DotsKit.Tool](https://www.nuget.org/packages/SawKing.DotsKit.Tool),
adds a service or a flag to a solution it has and upgrades a solution to a newer version of the template,
keeping the team's changes by a three-way merge:

```bash
dotnet tool install -g SawKing.DotsKit.Tool
dotskit new -N MyCompany -P MyProduct -S Orders   # a solution with its first service
dotskit new -S Billing --Storage true              # one more service
dotskit init                                       # describe a solution made with the template alone
dotskit upgrade                                    # bring a solution to the tool's version
```

[dotskit](https://dotskit.sawking.tech/docs.html#dotskit) in the documentation.

## Parameters

| Parameter | Default | What it does |
|---|---|---|
| `-N`, `--NamespaceRoot` | `MyCompany` | Organization name, the root namespace. May be dotted. |
| `-P`, `--ProductName` | `Product` | Product name. May be dotted. |
| `-S`, `--ServiceNameOrCustom` | `Service` | Service name. May be dotted. |
| `--Solution` | `false` | Generates the solution: `Common`, `All.sln` and the root files, with the first service. Without it, only the service folder, into a solution already there. |
| `--Database` | `postgres` | [Database](https://dotskit.sawking.tech/docs.html#persistence:sql-server): `postgres` or `mssql` (SQL Server). |
| `-H`, `--Hangfire` | `true` | [Background jobs](https://dotskit.sawking.tech/docs.html#jobs) on Hangfire. |
| `--Messaging` | `none` | [Message bus](https://dotskit.sawking.tech/docs.html#messaging): `outbox` or `direct`. |
| `-I`, `--Infisical` | `false` | [Secrets and settings from Infisical](https://dotskit.sawking.tech/docs.html#secrets). |
| `--Vault` | `false` | [Secrets and settings from HashiCorp Vault](https://dotskit.sawking.tech/docs.html#secrets:hashicorp-vault). |
| `--DiffApi` | `false` | [API contract diff](https://dotskit.sawking.tech/docs.html#api-diff) on pull requests. |
| `--GitHubCiCd` | `false` | [CI on GitHub Actions](https://dotskit.sawking.tech/docs.html#ci): build, tests on real servers, coverage on demand, secret scan. |
| `-FF`, `--FeatureFlags` | `false` | [Feature flags](https://dotskit.sawking.tech/docs.html#feature-flags). |
| `--HierarchyRules` | `false` | [Access rules over a tenant tree](https://dotskit.sawking.tech/docs.html#hierarchy-rules). |
| `--Storage` | `false` | [Object storage](https://dotskit.sawking.tech/docs.html#storage), S3-compatible. |
| `-CH`, `--ClickHouse` | `false` | [ClickHouse](https://dotskit.sawking.tech/docs.html#clickhouse): connections, schema check, readiness. |
| `--MongoDB` | `false` | [MongoDB](https://dotskit.sawking.tech/docs.html#mongodb) beside the main database: the client, the service's database, readiness. |
| `--Notify` | none | Channels of [notifications](https://dotskit.sawking.tech/docs.html#notifications): `email` - through SMTP or Graph API, with a sandbox outside Production; with `--Messaging` other services ask for it by a bus command. |
| `--TestFramework` | `nunit` | Test framework of the service's tests: `nunit` or `xunit` (v3). |
| `--Audit` | `false` | [Audit journal](https://dotskit.sawking.tech/docs.html#audit) of entity changes, through the outbox; with `--Messaging outbox`. |
| `--ApiGateway` | `false` | An [API gateway](https://dotskit.sawking.tech/docs.html#gateway) on YARP in place of a service, into a solution already there (without `--Solution`). |
| `--Deploy` | `compose` | [Deployment files](https://dotskit.sawking.tech/docs.html#deployment): `compose`, `k8s` or `none`. |
| `--Agent` | `claude` | [Rules and skills for an AI agent](https://dotskit.sawking.tech/docs.html#agents): `claude`, `opencode` or `none`. |
| `--HttpPort` | free port | Port in `launchSettings.json` and on the host under compose. |

Dotted names, running locally and the rest of the details:
[generating a solution](https://dotskit.sawking.tech/docs.html#generating).

## Versions

Version 2 targets .NET 8. To install a given version:

```bash
dotnet new install SawKing.DotNetSolutionKit::2.8.0
```

Do not apply version 2 with `--force` over a solution generated from version 1: namespaces, project references,
package management and the shape of error responses changed. What changed and how to carry it over:
[versions of the template](https://dotskit.sawking.tech/docs.html#upgrading). What each release brings:
[releases](https://github.com/sawking-tech/DotNetSolutionKit/releases).

## Source and license

[github.com/sawking-tech/DotNetSolutionKit](https://github.com/sawking-tech/DotNetSolutionKit), MIT.
Made by Vladimir Savkin at [sawking.tech](https://sawking.tech/).
