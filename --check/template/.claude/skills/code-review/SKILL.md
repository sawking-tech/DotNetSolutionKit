---
name: code-review
description: Review someone else's pull request in this solution, interactively - check it out, read the existing comments, review file by file, propose comments and never post them.
---

Review a pull request.

## Usage
`/code-review <PR number>` - e.g. `/code-review 63`

---

## How to review

These are an approach, not rules. They are what a reviewer most easily misses.

### 1. Top-down before bottom-up

Before reading the code, model how the domain *should* work:
- Which operations are there? What undoes each of them?
- Can an entity go through its whole life: created, used, retired, reused?
- Who loses what if an operation goes wrong?

Then read the code and look for the difference between the model and the code. Bottom-up finds tactical
bugs; top-down finds holes in the design.

### 2. Trust, then follow it through

Take the implementation as intended ("it was designed this way") and ask at once: "if that is so, where
does it lead?" If the rules of the game lead to an absurdity, either the machine is incomplete or something
was not thought through. Do not argue with the code first; play the game to the end and find where it
breaks.

### 3. A state machine is the specification

Read a state machine as the business requirements:
- Dead ends: states with no way back to a working one?
- One-way flows: do entities pile up with no way back?
- An escape hatch, a state that unlocks everything: intended or not?

### 4. Access is wider than "may the actor do X"

- Can the actor harm others, not only themselves?
- Asymmetry: A can give to B, but B cannot give back?
- Is there a "take back" for every "give"?
- Who loses visibility when the operation runs?

### 5. Meaning over syntax

One method doing two different things, one status meaning two different states: that is a design smell,
not a style issue.

### 6. How to write a comment

- **Show the code** when the fix is not obvious - show the pattern, do not say "rewrite it by the pattern".
- **Point at a reference** in the code base: "here it is done right", not "do it so".
- **Explain the consequence**, not the action: not "add a transaction" but "without a transaction the
  outbox row and the change commit separately; on a failure the event is lost".
- **Ask the intent before flagging**: "What is the business meaning of X?" before criticising the design.
- **Prove a silent failure with a fact**: not "looks like a bug" but "`Lines.Count` is always 0 because the
  query has no `Include`".

**Offer this approach to the reviewer** when they loop on naming, formatting or small patterns; ask "is the
method written right?" but never "is the method needed?"; ask nothing about inverse operations, the life
cycle or who loses what; or go file by file with no business context.

---

## Step 1 - Prepare the local copy

1. `git status` - with uncommitted changes, **stop and ask** whether to stash or commit them first.
2. If clean, fetch the PR branch unless it is already there:
   ```bash
   git rev-parse --verify -q pr-<N> >/dev/null || git fetch origin pull/<N>/head:pr-<N>
   git checkout pr-<N>
   ```
3. **Read every file from the local branch**, never through `gh api`: it is slower and costs more.

---

## Step 2 - Metadata and existing comments

```bash
REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
gh pr view <N> --json title,author,reviewRequests,reviews \
  --jq '{title: .title, author: .author.login, reviewers: [.reviewRequests[].login], reviews: [.reviews[] | {user: .author.login, state: .state}]}'
```

Establish the roles before reading code: the **author** (their decisions are intentional - ask before
flagging style), the **requested reviewers**, and who **already reviewed**. An author's reply to a
comment means it was acknowledged; a comment with no reply is still open.

Fetch all three kinds of comments:

```bash
# 1. Inline review comments, on lines - the most important
gh api "repos/$REPO/pulls/<N>/comments" --jq '.[] | {user: .user.login, path: .path, line: .original_line, body: .body}'

# 2. Review summaries
gh api "repos/$REPO/pulls/<N>/reviews" --jq '.[] | select(.body | length > 0) | {user: .user.login, state: .state, body: .body}'

# 3. General PR comments
gh pr view <N> --comments
```

For every comment, read the code at its line locally. Never interpret a comment without the code it is
about.

Summarise by file: who commented on which line, what they said, and the code. A block a comment already
covers thoroughly is **skipped** in the review and marked "covered by @reviewer".

---

## Step 3 - A quick scan, then ask

Skim the changed files and name what looks suspicious before asking:

> Before we go in - a few things caught my eye:
> - `OrderService.cs:42` - the method loads, validates, saves and publishes
> - `RefundService.cs:87` - the entity is changed outside the transaction
> - `OrdersController.cs:23` - `async/await` on a read
> Worth a close look?

Then ask the user (in their language):
1. Which file or part should get the closest look?
2. Is something worrying or unclear to you, and why?

**Stop here and wait for the answer.** Combine it with the flagged places into the review order.

---

## Step 4 - File by file, interactively

**Never read every file and dump everything at once.**

```bash
gh pr view <N> --json files --jq '.files[].path'
```

For **each file**, in order (the user's first, then by importance):
1. Read it locally.
2. Review it, method by method for a complex file.
3. Present what you found in **this file only**: what is clean, what is not, with line numbers.
4. **Stop and ask** whether to go deeper or move on. Follow the user into any block, method or line as
   long as they want.
5. Move on only when the user says so.

Keep a status table:

| File | Status | Notes |
|------|--------|-------|
| `src/.../OrderService.cs` | clean | |
| `src/.../RefundService.cs` | issues | no policy check |
| `src/.../OrdersController.cs` | skipped | covered by @reviewer |

Review against the solution's skills: `/add-entity` (aggregates, policies, three tiers),
`/add-service-class` (one job per method, transactions, idempotency), `/add-repository` and
`/add-specification` (queries), `/add-controller` (routes, permissions, status codes),
`/add-domain-event` (one class, one phase; raised on the aggregate), `/add-tests`.

**Tests - always check, even unasked:**
- a new service with business logic has a test class;
- a changed service method with non-trivial logic has a test that covers the change;
- a domain event handler has at least one test;
- missing tests for non-trivial logic are a blocker, not advice.

---

## Step 5 - Propose comments, never post them

For each issue, give the user one or two wordings:

```
File: RefundService.cs, line 42
Issue: the change happens outside BeginTransactionAsync

A (short):
"order.Refund(...) runs before BeginTransactionAsync - move it inside the try block."

B (with the consequence):
"order.Refund(...) is outside the transaction. If CommitTransactionAsync fails, the entity is already
changed in memory and the outbox row is not written. Move every change inside the try block, after
BeginTransactionAsync."
```

**Never post comments on GitHub for the user.** Present the options; the user decides and posts.

---

## Step 6 - Wrap up

Fetch the comments once more and compare them with everything discussed:
- **Covered** - found, and a comment was proposed or posted;
- **Already handled** - an earlier comment by another reviewer;
- **Missed** - in the comments but not discussed; flag it.

Final summary: files reviewed; issues and proposed comments; earlier comments skipped; what was not
covered.

```bash
git checkout -
git branch -d pr-<N>
```

If `git branch -d` refuses because the branch is unmerged, ask before `-D`.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
