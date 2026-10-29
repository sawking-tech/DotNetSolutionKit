# Contributing

## Branches

- `dev` is where the work goes. Every change reaches it through a pull request; it takes no direct pushes.
- `master` is the last release. It takes only the release pull request from `dev`, and a patch for the
  release when one cannot wait for the next.
- `2.x` is the .NET 8 line once 3.0 is out: its fixes go there through pull requests, until support for
  .NET 8 ends on 10 November 2026.

## Changes

A pull request into `dev` publishes nothing, so a small one is fine. Each pull request waits for a full run
of the checks, though, so it pays either to put related pieces of work into one pull request, or to keep
several pull requests going at once and work on one while the checks of another run.

- The pull request runs [template.yml](.github/workflows/template.yml): it generates a solution for each
  combination of flags, builds it, runs its tests and scans it for secrets. It has to be green before the
  merge.
- What the change generates has to be green too, and template.yml checks that as well. In the
  combinations with `--GitHubCiCd` it runs the workflows the solution ships - `ci.yml` with its test
  servers, integration tests and coverage threshold, `secret-scan.yml`, `api-diff.yml` - with
  [scripts/run-workflow.py](scripts/run-workflow.py), on the runner and nothing published. That their
  `uses:` steps run on GitHub itself is what the `full-dev` branch of
  [DotNetSolutionKit.Samples](https://github.com/sawking-tech/DotNetSolutionKit.Samples) shows: it is
  regenerated from `dev` the day after `dev` moves, and its CI runs then.
- A change in what a generated solution does comes with a test of that behaviour in the template's tests.
- A change to a workflow of this repository is run on a temporary branch first, for each case it
  handles - the one that succeeds and each one that refuses - before its pull request: a workflow that
  does not work never reaches `dev`, let alone `master`. A rehearsal publishes nothing.
- `dev` carries the next minor version with the label `-rc` in [version.json](version.json), as `2.8.0-rc`,
  and one entry of release notes for it. A change a user of the template sees adds a line to that entry, in
  the same commit, and leaves the number alone; a change that breaks solutions of the current major version
  and comes without a short way to update them makes it the next major, `3.0.0-rc`, with the update
  described in [upgrading](docs/getting-started/upgrading.md). The release notes name what the version is
  about, for whoever decides to update, in a sentence each; they are not a log of every change. The details
  are in the commits and the documentation, the steps to update in upgrading.
- Documentation and the site (`docs/`, `index.html`, `docs.html`, `site/`) change in the same pull request
  as what they describe. The site is published from `master` by [site.yml](.github/workflows/site.yml),
  which promote.yml starts once master has moved; it writes the notes of the released versions from
  `version.json` into `index.html`, so the site describes the released version.

## Definition of done

A task is done when its issue's own "Done when" holds and so does every line here that applies to it.

- `template.yml` is green on the pull request.
- A change in behaviour comes with its test, in the same commit: what a generated solution does - in the
  template's tests or `template.yml`.
- A change to the template is checked on a generated solution: the flags it touches generated on and off,
  the solution built with `-warnaserror`, its tests passed.
- A change a user of the template sees is in the docs, in English and in Russian (`<name>.ru.md`, listed in
  `site/config.js`), in the README and in `docs/roadmap.md`, and adds a line to the `-rc` entry of
  `version.json`. A breaking one waits for the next major version and is described in
  [upgrading](docs/getting-started/upgrading.md).
- A change in what a generated solution gets, or in how it is generated, is in the skills the solution
  ships, `scaffold-service` first.
- A change to a workflow ran on a temporary branch first, for each case it handles.
- What a text says about the code - a doc, a comment, a commit message - is checked against the code; no
  name of a client or of its product appears anywhere.
- Before the pull request, an agent that knows only the issue and the diff reviewed it, and each finding is
  fixed or answered.

## Releases

The template follows semantic versioning. Its version and release notes are in `version.json`, in the same
shape as a generated solution's ([ADR-004](docs/adr/004-product-version-by-hand.md)).

What `master` receives is published: the GitHub release, the package on nuget.org and in GitHub Packages.
A release comes about once a week, at the end of a sprint, when the work done is worth one, and only
then:

- inside the template: `template.yml` is green on the last commit of `dev`;
- outside it: `full-dev` in DotNetSolutionKit.Samples is regenerated from that commit, and its CI is green;
- the life of a solution: the branch `lifecycle` of the samples is built from that commit - a solution made
  with the template alone two minors back, then described, upgraded and grown by `dotskit` - and its CI is green;
- the batch is finished: nothing begun and left undone is in `dev`.

Before the release, one commit on `dev` takes the label off: `2.8.0-rc` becomes `2.8.0`, and so does its
entry of notes; [promote.yml](.github/workflows/promote.yml) refuses a version that still has it, and the
site shows no labelled version. The minor version grows by exactly one from release to release. The notes
have a headline naming the change a user of the template gains most from, and lines checked against the commits
since the last release. After the release, `dev` takes the next minor with the label.

The release is then one pull request from `dev` into `master`.

Merged into `master`, it makes [release.yml](.github/workflows/release.yml) tag `v<version>`, wait for
`template.yml` on that commit and publish: the GitHub release with the version's notes
(`scripts/release-notes.sh <version>` prints the same text), the package
`DotNetSolutionKit.Templates.csproj` packs, on nuget.org as `SawKing.DotNetSolutionKit`, and the tool
`dotskit` of the same version, packed from `tool/DotsKit` as `SawKing.DotsKit.Tool`; both are attached to the
release and published on nuget.org, the tool first, and in GitHub Packages. nuget.org takes them by
Trusted Publishing, with no stored key: the policy of the profile `sawking.tech` names this repository and
`release.yml` and covers `SawKing.*`. A version on nuget.org cannot be deleted, only unlisted. DotNetSolutionKit.Samples regenerates its release branches from the new tag within a day.

A patch version is only for a fix that cannot wait for the next release. If `dev` can be released as it is -
what is new in it is finished or behind a flag - the fix goes into `dev` and `dev` is released. If it
cannot, the fix is a pull request into `master` that raises the patch version, as `2.7.1`, with its own
notes; once released, `master` is merged into `dev`. A gap in patch versions is allowed.

`dotskit` upgrades a solution one major version at a time: dotskit X.y takes every minor of X and the last
minor of X-1 (`TemplateVersions` in `tool/DotsKit`), and the code it keeps for an older base names in a
comment the major version that removes it. The template's own packages move by this policy: a patch after
7 days, a minor version after 30 days, a fix of a vulnerability at once, a major version only with a major
version of the template, and never a version under a licence the template cannot ship.

A fix of the .NET 8 line is a pull request into `2.x`; `2.x` releases `2.*` versions only, and their GitHub
release is not marked latest.
