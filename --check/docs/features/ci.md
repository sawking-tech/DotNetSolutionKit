# CI on GitHub Actions

Generated with `--GitHubCiCd`, off by default: each team has its own CI, and these workflows only run on
GitHub. Pass it with `--Solution`; the workflows cover every service in `All.sln`, so a later service needs
nothing more.

## `.github/workflows/ci.yml`

Runs on every pull request, on a push to `main` or `master`, and by hand, with coverage when asked for:

1. Fails when a service project is missing from `All.sln`. Generating a service adds it there; a project
   added by hand and forgotten would drop out of the build and its tests without anything failing.
2. Builds `All.sln` in Release.
3. Runs the unit tests: everything outside the category `TestCategory=Integration` (`[Integration]` in a service, `[Category(TestCategories.Integration)]` in `Common`).
4. Runs the integration tests against real servers started in the job by `tests/servers/up.sh` - the same
   scripts a developer runs: PostgreSQL or SQL Server always, ClickHouse with `--ClickHouse`, MongoDB with
   `--MongoDB`, MailHog with `--Notify email`, SeaweedFS with `--Storage`, Vault with `--Vault`. They start
   before the build and are waited for before the integration tests. PostgreSQL keeps its data on a ramdisk
   with durability off, which is safe only because the data dies with the job. See
   [testing](../architecture/testing.md).
5. With coverage asked for, measures it over both runs: a report per service and one for `Common`, a summary
   of each on the run page, the HTML reports as an artifact. Left out is wiring with no logic of its own:
   migrations, model configuration, DI composition and its extensions, host setup, seeding, controllers -
   a controller holds no logic - and the security handlers and filters, which the integration tests cover.
   The gateway's report keeps its transforms. The reports are made even when a test failed.

A pull request does not measure coverage: with thousands of tests, measuring every branch on each pull
request costs more than it tells, and what a pull request needs is tests of the code it changes, which the
review checks.

Branch coverage fails the run when a report is below the repository variable `COVERAGE_MIN_BRANCH`, in
percent; a report with no branches at all, a new service with nothing yet to decide, is not held to it.
Without the variable the reports are shown and nothing fails on them, so a team sets the
threshold once its tests exist, and raises it without editing the workflow.

## `.github/workflows/coverage.yml`

Started by hand, on the branch chosen when it is started: `ci.yml` with coverage on. It shows the coverage
of a branch before its pull request. `ci.yml` can be called with coverage on by another workflow too
(`workflow_call`, input `coverage`).

## `.github/workflows/secret-scan.yml`

Runs [gitleaks](https://github.com/gitleaks/gitleaks) over the commits a pull request or a push brings in,
with `tools/gitleaks/.gitleaks.toml`: the gitleaks rule set plus connection-string passwords, URIs with
credentials and signing keys in JSON settings. Values marked `test-do-not-use`, `placeholder` or
`changeme`, and `${VARIABLE}` references, are allowed.

Only incoming commits are scanned, so a finding in old history does not fail every later run. To scan the
whole history once, for example when adopting the workflow in an existing repository:

```bash
gitleaks detect --config tools/gitleaks/.gitleaks.toml --redact
```
