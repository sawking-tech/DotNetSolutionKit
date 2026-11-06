# API diff

Generated with `--DiffApi`, off by default. Pass it with `--Solution`, and to each service generated later.

On every pull request, CI generates the OpenAPI document of each service twice: from the pull request's
branch and from the branch it targets (usually `main`). [oasdiff](https://github.com/oasdiff/oasdiff)
compares the two. The report goes into an artifact of the run, and a breaking change fails the check
unless it is declared.

Nothing is committed: both documents are generated from code during the run, so no snapshot can go stale.

The pull request's own branch is always strict: if its documents cannot be generated, the check fails.

The target branch gets an exception, with a cost. Sometimes the target has no documents to compare
against: it comes from a version older than the contracts, or its schema generation is broken. The check
then prints a notice naming the service and the reason, and passes - otherwise the pull request that
repairs the generation could never merge, blocked by the very break it fixes. The cost: until that repair
lands, no pull request is compared, and the notice in the log is the only sign of it.

## Generate the documents locally

```bash
bash scripts/generate-api-schemas.sh
```

writes `src/services/*/*.API/api-schema/<service>.json` for every service. Each service is run with
`--dump-schema` in schema-only mode, so no database, broker or secrets are needed. How and why:
[ADR-003](../adr/003-api-schema-generation.md). An [API gateway](api-gateway.md) is passed by: it serves the
services' documents and has no contract of its own.

## Declare a breaking change

`oasdiff` sees two documents and nothing else. When a change it reports as breaking is intended, for
example a path leaving the document while its route keeps answering, declare it in
`.github/api-diff-ignore/<service>.txt`, one line per finding, with the reason above it:

```
# The legacy export left the document; the route still answers until 2.0.
1.4.0 GET /api/v1/orders/export
# The status became an enum; every value already sent is in it.
1.4.0 PUT /api/v1/orders/{id}/status request-property-became-enum
```

The fields are the version, the method, the path and, optionally, the check id, all copied from the log of
the job. Method and path accept `*`. Without a check id the line covers every finding on that method and
path.

A line applies only while `version.json` holds its version. Once the version is raised, the base branch
already has the change and the line could only hide a later regression on the same path, so it stops
applying by itself.

A path that is really going away is a breaking change for its callers: deprecate it, release that, and
remove it in a later release, rather than declaring it here.

## What runs where

| Part | Where |
|---|---|
| Schema-only mode, `SchemaDump` | `Common.Web/Setup` |
| `SchemaHost.Build` | each service's API project |
| Generation and normalisation | `scripts/generate-api-schemas.sh`, `scripts/normalise_api_schema.py` |
| The check | `.github/workflows/api-diff.yml`, on pull requests |

The workflow is for GitHub. On another CI, run the script on both sides and compare the files with oasdiff
in the same way.
