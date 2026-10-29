# ADR-004: The product version is set by hand

**Status:** Accepted, 2026-10-03

**Author:** Vladimir Savkin, the creator of DotNetSolutionKit

## Context

A generated solution needs to answer two questions about what is running: which release it is, and which
code it was built from. The common answer in .NET is a versioning library, Nerdbank.GitVersioning,
GitVersion or MinVer, that derives the version from git history at build time. Version 1 of the template
shipped Nerdbank.GitVersioning.

Those libraries solve two problems:

1. **A unique number for every build.** A package published to NuGet needs it: two different contents
   under one number break whoever depends on the package. A service is not published as a package.
2. **A number without a person deciding it.** A number derived from the commit height says nothing about
   compatibility, and compatibility is what semantic versioning is read for. Only a person knows whether a
   change breaks a caller.

So for a service the library duplicates what the commit already tells, and does not provide what a version
is for. It adds a build dependency and needs the full git history, while CI usually clones shallow.

## Decision

The version is written by hand in `version.json` at the solution root, next to its release notes:

```json
{
  "version": "1.2.0",
  "releaseNotes": {
    "1.2.0": { "headline": "...", "highlights": ["..."] }
  }
}
```

`Directory.Build.props` reads the version from the file and stamps it on every assembly. The commit is a
separate field: the short SHA recorded at build time, or `GIT_SHA` from the environment when the build has
no `.git` folder, as in a Docker build. `/health` and `/ready` report both, with the release notes.

The version changes when a person decides a release is out, and the same commit explains what is in it.
Release notes are written by people anyway; writing them in the repository, in the commit that raises the
version, is the shortest way.

| Question | Answered by |
|---|---|
| Which release is running? | `version` in `/health` |
| Which code was it built from? | `commit` in `/health` |
| What changed in this release? | `releaseNotes` in `/health` and in `version.json` |
| What is running on my machine? | the working copy the developer runs |

## Alternatives considered

**Nerdbank.GitVersioning, GitVersion, MinVer.** Rejected for the reasons above. A scheme that raises the
major version from a `BREAKING CHANGE:` footer in a commit message still depends on a person writing the
footer: it moves the decision into commit messages and does not take it away.

**The commit inside the version (`1.2.0+abc1234`).** Rejected: two builds of the same release from
different commits would carry different versions, and comparing versions would compare commits. The
commit has its own field.

## Risks and how they are handled

The template does not know where a generated solution will be hosted or how it will be built. So the
handling it can rely on is the team's process; checks in CI are an addition for those who use them.

| Risk | What happens | Handled by | Possible CI check |
|---|---|---|---|
| The version is not raised | A new release goes out under the old number; the version drifts from the code it names | Raising the version is part of preparing a release; the review of a release checks `version.json` | A pull request check: code under `src` changed and `version.json` did not |
| The wrong level is raised: a breaking change ships as a minor release | Consumers believe they are still compatible, for example on 1.x when the code is 2.0, and break on update | Review; the [API diff](../features/api-diff.md) with `--DiffApi` shows every change of the API contract | The API diff fails a pull request on a breaking change of the contract until it is declared, and a declaration expires with the next version. Behaviour, configuration and the database schema have no such check |
| Release notes do not match the release | Readers of `/health` and of the notes get the wrong story | The notes live in the same file and the same commit as the version, so a review sees both | none |

The checks in the last column are known options, not part of this decision. The template may later
generate them for GitHub only, behind a flag.

No versioning library covers the second risk either: none tells a breaking change from a compatible one.
The cost of setting the version by hand is the discipline to raise it at the right level.

## Consequences

- No versioning package in the build and no need for the full git history.
- The version is a decision with a written reason next to it.
- Raising it, and raising it at the right level, relies on the team's process.
- This ADR is about the version of a generated product. The template itself is versioned with git tags;
  see [versions of the template](../getting-started/upgrading.md).
