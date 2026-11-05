# DotNetSolutionKit agent guide

Rules for an AI agent working in this repository. The product is a solution generated from the template:
check each change by its effect in a generated solution.

## Project

DotNetSolutionKit is a configurable `dotnet new` template (package `SawKing.DotNetSolutionKit`) with a
companion CLI, `dotskit` (package `SawKing.DotsKit.Tool`). The template generates a .NET 8 microservice
solution for DDD and Clean Architecture: shared `Common` libraries, services split into Domain /
Application / Infrastructure / API projects, and optional capabilities selected by flags. Capabilities
cover databases, Hangfire, messaging with outbox, Infisical or Vault secrets, feature flags, object
storage, ClickHouse, MongoDB, email, audit, a YARP API gateway, CI, and Docker Compose/Kubernetes
deployment. The template carries no business logic or runtime of its own.

`dotskit` adds services and flags to a generated solution and upgrades it with a git-based three-way
merge that preserves the team's changes. Its manifest is `.dotskit/manifest.json`.

`version.json` holds the version and release notes in English and Russian. The source carries the next
minor with an `-rc` label. `dev` is the working branch; releases are published from `master` on a tag.

## Layout

```
template/                  # the dotnet new template, packed as SawKing.DotNetSolutionKit
├── .template.config/      # template.json (parameters, conditions), dotnetcli.host.json (short names)
├── src/common/            # Common, .Application, .Contracts, .Infrastructure, .Web, .Testing, .Tests
├── src/capabilities/      # a project per flag, Capabilities.<Name>, each with its own tests
├── src/services/          # the service named by -S, split by layer, with tests and All.sln
├── gateway/               # the API gateway of --ApiGateway
├── deploy/, tests/servers/, .github/   # what a generated solution ships beside its code
├── .claude/, CLAUDE.md    # rules and skills a generated solution's agent gets
└── version.json, Directory.Build.props, Dockerfile
tool/
├── DotsKit/               # the dotskit CLI: Commands/ Generation/ Merging/ Planning/ Solutions/
└── DotsKit.Tests/         # NUnit + Shouldly tests
docs/                      # every page has a Russian twin <name>.ru.md
site/                      # the static site, built by scripts/build-site.py
scripts/                   # repository checks and the site build
.github/workflows/         # template.yml checks PRs; promote, release and site publish
DotNetSolutionKit.Templates.csproj   # packs template/ into the template NuGet package
version.json               # product version and release notes
CLAUDE.md, AGENTS.md       # identical agent rules
```

## Build and test

`.github/workflows/template.yml` generates solutions for flag combinations, builds and tests them, and
scans for secrets. With `--GitHubCiCd`, it also runs the generated solution's workflows through
`scripts/run-workflow.py`.

Check a template change locally on a generated solution:

```bash
dotnet new install ./template --debug:custom-hive /tmp/hive --force
dotnet new DotNetSolutionKit -N Acme -P Shop -S Orders --Solution -o /tmp/acme --debug:custom-hive /tmp/hive
dotnet build /tmp/acme/src/services/Acme.Shop.All.sln -warnaserror

eval "$(bash tests/servers/up.sh)" && dotnet test src/services/*.All.sln --filter "TestCategory=Integration"
```

Build and test the tool:

```bash
dotnet build tool/DotsKit/DotsKit.csproj -warnaserror
dotnet test tool/DotsKit.Tests/DotsKit.Tests.csproj
```

Run repository checks:

```bash
scripts/check-style.py            # file bytes vs .editorconfig; --fix repairs line ends/BOM/trailing spaces
scripts/check-json-metadata.py <solution>   # JSON _comment_ keys (ADR-006)
scripts/check-doc-anchors.py      # docs anchors
scripts/check-agent-files.sh      # rules and skills of a generated solution
scripts/check-service-layers.sh <solution> <service>   # add a use case to a copy, build and test it
python -m pytest scripts/test_check_style.py
```

`dotnet pack DotNetSolutionKit.Templates.csproj` packs the template. The `WriteTemplateVersion` target
stamps the version from `version.json` into a copy of `template.json`.

## Code rules

The rules apply to the template and `scripts/`. Prove repository rules here before moving them into the
rules shipped to a generated solution's agent. One style is owned by the formatters (ADR-007); CI rejects
file-byte violations and C# style violations in the tool.

- Code, comments, XML docs and commit messages are in English.
- Keep dependencies pointing inward. A module depends only on what it needs; decision layers do not know
  storage or presentation layers.
- Prefer composition: put behaviour behind an interface, inject it through the constructor, and compose
  it in one place instead of overriding `virtual` members.
- A method does one thing. An orchestrator names its steps and calls small methods or classes.
- Replace meaningful inline numbers, paths, patterns and names with constants owned in one place.
- Keep decision logic apart from files, processes and console I/O so it can be tested without them.
- A behaviour change includes its test in the same commit; `git checkout <sha>` builds and passes.
- An expected failure produces a one-line message with the problem and remedy, not a stack trace.
- Before adding a kind of file the repository already has, read two or three neighbours and follow them.
- Files use UTF-8 with LF, a final newline, no trailing whitespace and no indentation tabs. BOM is allowed
  only in solution files. Code contains no Cyrillic except in test data. `check-style.py` enforces this.
- JSON written by the template or `dotskit` explains fields with `_comment_<Key>` metadata keys (ADR-006),
  including intentionally empty fields. `template.yml` checks this.
- Template XML files never contain two hyphens inside a comment: MSBuild silently skips invalid files
  imported by mask.
- Conditional blocks use their file's comment syntax: `//#if` in C# and JSON, `<!--#if -->` in XML, and
  `#if` on its own line in YAML, `.env.example` and `.sln`.
- Use shell for scripts that mainly run commands and Python standard library for scripts that read text.
  Use C# for the product. The Python scripts move to C# files run by `dotnet run` in 4.0 on .NET 10.

## Template and tool changes

- Verify template changes on a generated solution with every affected flag on and off, `-warnaserror`,
  and passing tests.
- A flag is whole: `template.json` excludes its files when off, shared-file blocks use one condition, CI
  generates a combination with it, and it has English and Russian documentation. A supplied flag that does
  nothing must say so at generation through a post-action.
- Put new flag functionality in `Capabilities.<Name>` under `src/capabilities`, referenced only by the
  services that use it.
- Solutions generated by earlier versions keep working. Document required user action in
  `docs/getting-started/upgrading.md`; breaking changes wait for the next major version.
- Add sample variants for the next release to `.samples/variants.json` in `DotNetSolutionKit.Samples`.
- Update template package versions by policy: patch after 7 days, minor after 30 days, vulnerability fixes
  immediately, and majors only with a template major.
- Unit-test the tool's decision logic without the file system or git where possible. Code supporting old
  template bases names the major version that removes it. `dotskit X.y` upgrades across every minor of X
  and the last minor of X-1.

## Commits, branches and releases

- Create a working branch from `dev` before committing. Never commit or push directly to `dev`; all changes
  reach it through a pull request with green CI. `master` takes release PRs from `dev` and urgent patches.
  After 3.0, `2.x` is the .NET 8 maintenance line.
- A user-visible change updates English and Russian docs, `site/config.js`, README, `docs/roadmap.md`, and
  the current `-rc` entry in `version.json` in the same PR. Only a release changes the version number.
- The repository is public: never name clients or their products in code, comments, docs or commits.
- Check statements about code against the code.
- Rehearse a repository workflow change on a temporary branch for each case it handles before its PR.
- Releases use semantic versioning, about weekly at sprint end. `promote.yml` refuses a labelled version;
  `release.yml` tags `v<version>`, publishes notes from `scripts/release-notes.sh`, and publishes both
  packages to nuget.org and GitHub Packages. `site.yml` publishes the site from `master`.
- Definition of done: `template.yml` is green, behaviour tests accompany the change, docs and release notes
  are updated, generated-solution skills are updated where needed (`scaffold-service` first), and an agent
  that knows only the issue and diff reviews the PR.
- Every commit on `dev` ships green: a red check is a stop, not noise. Find the cause to its root and fix it
  in the source, not where it surfaced. Never merge or push on top of red, and never restart a failed run
  without a fix — a bare restart only proves the failure is deterministic. Cancelled, stale or hung runs
  count as red until explained.

## Security

- Generated solutions ship `secret-scan.yml`; their first push and CI generation here must pass it.
- Never write secrets to files. Keep them in Infisical or HashiCorp Vault and in memory; read settings on
  each use where they may change without redeployment.
- `dotskit` never commits a merge, pushes or checks out a branch. A failed upgrade is reverted with
  `git reset --hard`. Neither merge writes files ignored by the solution's git.
- CI uses minimal permissions. nuget.org publishing uses Trusted Publishing without stored keys. A NuGet
  version can be unlisted, not deleted.
