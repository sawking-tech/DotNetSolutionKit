# ADR-008: No AI attribution in a commit — anywhere, ever

**Status:** Accepted, 2026-11-07

**Author:** Vladimir Savkin, the creator of DotNetSolutionKit

## Context

The project is written with an AI model as a tool, and [Working with AI agents](../getting-started/agents.md)
states the rule: there is no attribution and no co-authorship of the model in the commits or in the files.
The model is a high-level compiler and generator; neither an operating system nor a compiler is named as a
co-author, and the model is in the same role.

On 2026-10-28 the rule was broken in public: five pull requests of the 2.8 line (#123–#127) were
squash-merged through the GitHub web interface, the messages carried `Co-Authored-By: Claude Opus 5.5` and
`Claude-Session:` trailers from the branches, and GitHub showed "Vovanda and claude authored" on the history
pages of dev and master, next to the release commits.

## Decision

No commit of the project — in any branch, in any repository of it — carries attribution of an AI model:
no `Co-Authored-By`, no session links, no "generated with" lines, in the message or in the trailers.

A pull request that adds such a commit does not pass review: the review checks the messages of the branch
before anything else, and attribution is a blocking finding, however small the pull request is. A branch is
checked before a squash-merge through the web interface too, because the merge copies the trailers of the
source branch into the commit it creates, and no hook runs there.

## How the risk is held

The agent that writes the commits works under a skill that forbids attribution in the message and checks
the final text before every commit and push. A `commit-msg` hook in the repository greps the message for
attribution markers — co-author lines, session links, the names of models and agents — and rejects the
commit. The two hold what a person controls; the review gate above holds the one place they do not reach,
a squash-merge through the web interface.

If attribution slips through into a shared branch anyway, it is not left standing: the branch is rewritten
to remove it, and every commit above the rewritten one is rebased and loses its hash. What pointed at the
old hashes rots — the packages on nuget.org lose their source link, links in issues and notes stop
resolving. That damage is exactly why slipping attribution through is unacceptable: one carelessly merged
pull request costs the history of the whole line.

The five attributed commits of 2026-10-28 are the first application of this decision: before the release of
2.9, dev and master are rewritten once, the trailers are removed from the five messages, every tree, author
and date stays as it was, both branches and the tag v2.8.0 are force-pushed, and this ADR is the public
record of why. The rewrite is a one-time repair, not a precedent: from here the history of dev and master
is append-only again — because the gate above keeps attribution out, not because a repair is cheap.

## Alternatives considered

- Leave the five commits. The rule is broken in public on the first page of the history, next to the
  release commits.
- Revert them. A revert adds commits on top; the attributed ones stay and keep showing.
- Accept attribution as the norm. It contradicts the role the model holds in this project — a tool, not an
  author — and advertises on every page of the history what the project does not believe.

## Cost

The packages 2.8.x on nuget.org carry the repository commit of the old history: their source link opens a
commit that no longer exists. GitHub keeps the old commits on the pages of their pull requests. The price
is the cost of the mistake of 2026-10-28, it is paid once, and the gate exists so it is not paid again.
