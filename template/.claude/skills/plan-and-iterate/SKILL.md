---
name: plan-and-iterate
description: The plan of long work - a list of actions, rebuilt at any time from the tracker's issues; decisions, agreements and their reasons do not live in it. Eight rules at the top of the file are never broken, and plan.mjs check checks the plan's form. Code, an investigation, a document, an analysis - the structure is the same.
---

Discipline for long work of any kind: moving a code base, a migration, a refactoring, a
long investigation, a large document, taking apart someone else's system, a report. The structure is the
same.

## The rules - never broken

If even one is broken, the skill is not applied: fix the plan before the next step of the work.
`node .claude/skills/plan-and-iterate/plan.mjs check` at the start and at every mark; on exit code 1, the plan is fixed first.

1. **The plan is a list of actions.** The test: the plan is deleted - what is lost? The answer is "nothing":
   the queue is rebuilt from the tracker's issues, and the working memory is one current item. If something
   is lost, it lived in the wrong place.
2. **Decisions, agreements and their reasons do not live in the plan.** A decision goes to the issue or an
   ADR, a rule to the docs or CLAUDE.md, an agreement with a person ("said that ...") to memory. What stays
   of them in the plan is `issue=` or an item that is an action. Otherwise the plan becomes a warehouse that
   nobody reads to the end, and the decision is lost with the first cleanup.
3. **`why`, `how`, `done_when` - one line each, up to 160 characters.** `how` says what we do, not why it was
   decided so and who said it. An analysis a paragraph long goes to the issue: these three swell first.
4. **A `check` is only what is run and gives a `got`.** It is written before the run, the fact is added
   after. An agreement or a reminder written as a `check` has nothing to close it by, and it hangs forever.
5. **One item with `now=true`, and it has `since`.** Everything above it in the file is closed: the order of
   the file is the priority, and an open item at the top reads as done.
6. **The plan is edited first, then the work.** An accepted remark becomes an item or moves the marker. An
   agreement that did not touch the plan is the same as a disagreement: the item you were on is lost.
7. **Closed items are deleted, far ones are not written out.** The plan is up to 100 lines. A far item is a
   line with a link to the issue; it is expanded when reached, or half of it is stale by then.
8. **One task in work until it is finished.** What is noticed on the way is an item in the queue, not an
   edit in the current one: work dropped in the middle looks finished, and it cannot be resumed without its
   context.

Everything below is the details and the reasons for these rules.

## What the plan is

**A list of actions: the queue of what we do, plus the working memory of the current item.** Not a log, not
an archive of decisions, not a wish list.

The queue answers "what to work on": items by importance, a marker on the current one. The working memory
answers "what I know about the current item": the approach, what turned up, what I tripped over. It holds
the progress, so what is done is not checked again and what was ruled out is not tried again. Memory blurs
within a few hours; the plan does not.

**The plan lives in a file on disk, outside the conversation and outside the repository.** In the chat it
dies with the session; committed, it travels to every copy as someone else's clutter. Its place is
`.claude/session-context/`, which `.gitignore` keeps out of the repository.

**The disk is the source of truth, always.** Only what outlives the plan goes elsewhere: a decision taken,
an analysis, a technique - into the issue tracker or the docs. The plan has no second store.

**One task, one plan document.** Do not start a new one: read the existing one first and make sure there
is nothing to edit. Otherwise in a week the same thing exists in three versions.

**Why KDL.** A Markdown checklist has one structural token, `- [ ]`: an item has no boundary and no address,
it can be referred to only by its text, and the text drifts when rewritten - which is how items get lost. A
KDL node has an address and its state as a property, is found with one `grep`, and is edited in place.

## Item states and transitions

The states exclude each other: one at a time.

| State | Attribute | Entered when | What must appear on entry |
| --- | --- | --- | --- |
| queued | none | the item is added | `why`, `how`, `done_when` |
| in work | `now=true` | taken into work | `since`; the marker removed from the previous item - there is one per plan |
| blocked | `blocked="reason"` | waits for someone's answer or an event | an item that unblocks it (rule below) |
| closed | `done=true` | `done_when` is met | `at`; free notes moved to the issue, `issue=` stays |
| dead end | `dropped="why"` | the path was tried and does not work | the reason in words; survives the cleanup of closed items |

**The transitions most often skipped:**

- **into work -> always `since`**, also when the marker was left from the previous session: nothing moved
  there, so the stamp would never appear. A `now=true` without `since` gets the current time.
- **`blocked` -> into work: `since` is reset.** Otherwise the block counts as work.
- **`ok=false` on a check -> a new item in the plan.** A mismatch is fixed, not noted as "seen".
- **`blocked` does not mean "start the neighbour".** Go to the next item in order; unblocking is an item of
  its own with its own `done_when`, or nobody does it.

## Attributes that are not states

| Attribute | Meaning |
| --- | --- |
| `since="2026-08-30 03:36"` / `at="..."` | stamps on the transitions: taken into work and closed |
| `depends="other-item"` | the current item is pointless without it; set where the order is not obvious |
| `issue="#88"` | the link to the tracker's issue; stays in place of the notes that moved there |
| `state="in words"` | only on a large node: it aggregates sub-items, and one state is not enough |

`state` on a single step is a hole in the state machine: everything that did not fit goes there, and in a
month it has its own vocabulary. If a state is missing, add it to the table or split the item; do not
describe it in words.

**About the stamps.** Local time, to the minute. **Never set after the fact:** a stamp reconstructed from
history measures neither the time in the queue nor the pauses between sessions, and in the file it cannot
be told from a measured one - the first statistic would be computed on the mix. Read the stamps as a
calendar interval: items overlap at the start, so the sum is longer than the session and does not add up to
"time spent".

## Inside a node

```kdl
step order-number-from-sequence "An order's number comes from a sequence, before the order exists" issue="#131" {
    why "The order-created event carries number 0: the number is assigned by the database after the insert"
    how "Take the number with IShortIdGenerator before the constructor; the migration creates the sequence"
    done_when "an order created through the API has its number in the event the consumer receives"

    check "a created order has a number" got="ORD-1042 in the response and in the outbox row" ok=true
    check "two orders created at once get different numbers" got="1043 and 1044" ok=true
    check "a rolled-back create leaves a gap, not a duplicate"
}
```

- **`why` / `how` / `done_when`** - one each on **every** item, **one line each**. An analysis a paragraph
  long goes to the tracker: these three are the first to swell. Without `done_when` an item is an
  intention, not a plan: it can only be closed by eye. `how` is what we do, not why it was decided so and
  who said it.
- **`note`** - a free note, **only on the item with `now=true`**. It needs no limit; the limit comes by
  itself, since there is one current item. A swollen item that is not current is a symptom: the far work
  was written out before it was reached.
- **`check` / `got` / `ok`** - a check and its result; not a place for an agreement or a reminder - what
  cannot be run to give a `got` is not a `check`. `check` is written **before** the run and stays bare,
  like the third one above; `got` and `ok` are added after. Writing both at once is making up the fact
  together with the expectation.

**The id is opaque** - a slug, not a number. A hierarchical number conflicts with inserting by importance:
the place between `1.159` and `1.16` is taken by adding a digit, and `1.1595` is born. Priority is carried
by the position, so the id must mean nothing.

## What lives where

| Where | What goes there |
| --- | --- |
| the plan | the queue, the states of the items, the working memory of the current one |
| the issue tracker | the history: the symptom, the cause found, the analysis, the rejected option, the decision |
| an ADR, the docs, memory | a decision taken, a rule, an agreement with the other person ("the user said ...") |
| a run log | an audit of the rounds, where the work goes in repeated passes |

**A plan rewritten whole always has a backup:** first a copy in `raw/plan-<date>.kdl` next to the plan, then
the new file and `plan.mjs check`. Before the old one is deleted, a comparison: every decision in it has
found its place.

**The plan holds the queue, the tracker the history.** The plan's header is not a log of decisions: it holds
only the path to the tracker and, if there is one, the deadline; a comment "decided on the 6th that ..." is
a sign that the decision did not reach its place. Closed items leave the plan; what is worth keeping
moves to the issue. Appending to the end and keeping closed items is what turns a plan into a warehouse:
one grew to 2730 lines.

**Far work is not written out in advance** - it stays an issue and is expanded when reached. Written out
early, half of it is stale by then.

## How it is read and shown

**The order of the document is the priority.** Everything above the current item is closed; an open item
left above reads as done - and is forgotten. Before marking: is there an open item above the current one?
If there is, it is either finished or moved down.

**New items are inserted by importance, not at the end:** first what blocks the next step, then what is
expensive to fix later. Improvements with no one asking go to the end; that is where they usually stay.

**The order minimises the path.** Every place in the queue has a reason: what is done earlier saves actions
later. What would have to be redone or repeated later goes before what causes the repetition: a feature
before the migration that would otherwise pass over it a second time; a question whose answer will be needed
later - now, before it becomes a blocker. An item whose place has no reason is moved.

**Where I am** - `node .claude/skills/plan-and-iterate/plan.mjs where`: the current item in full, the next
ones as numbers with slugs, a count of violations at the bottom. Without the tool: `grep -n "now=true" <plan>`.

**In an answer the plan is shown as plain numbers, not slugs:** "1, 2, 3" in document order from the
current item, five to seven items. The numbering lives only in the answer. A slug in an answer reads harder
than a number, and avoiding the number is why slugs were introduced.

## The line

**Agreement without grounds moves the marker; it is not a line in an answer.** Agreeing is the way to drop
what was started, only with permission: agree with a remark - move the marker - lose the item you were on.
Models do this systematically, which is why the rule lives here and not in manners.

An agreement must leave a trace in the file: an accepted remark **becomes an item** in the queue, or it
cancels the premise of the current item, and then the marker moves at once. **Agreeing without touching the
plan is the same as not agreeing.** An objection is said briefly and with a fact, but an accepted decision
lands in the plan too.

Where `depends` is not set, the one doing the work chooses the order - and takes what the next step cannot
do without, not what is more interesting.

**The plan is kept by the one doing the work, without asking** - neither for an item's wording, nor for a
reordering, nor for moving the marker. Asking for permission for that is a way to stand still. The other
person is asked about a fork in the work itself, not about how to write it down.

**The plan is dynamic.** It takes in the traps (the part turned out bigger than it looked), the decisions
said aloud ("noted", "that goes there"), and the idea of how to approach. Not written down - not decided.
Changed your mind - rewrite it: that is the ordinary course of work, not a sign of a mistake.

## Rounds instead of one pass

Where the result is not known in advance - a migration, someone else's system, a repeated run:

1. Take a step.
2. Compare it with the expectation, do not just "look".
3. On failure, fix it **at the source**, not where it showed.
4. Repeat the round the same way, with the same check.

**An audit of each round**: the symptom, the cause, what was fixed and where, in which round the trap went
away. A round without traps is written down too - it shows the part was passed, not skipped.

## The check stage

**One stage of work, one stage of checking**, right when the stage is done. Do not wait for the end: what
piles up has nobody and no time to check it, and finding which of ten changes led to the bad result costs
more than checking each in place.

| What the stage is | How it is checked |
| --- | --- |
| something shown on a page | a smoke test: the route by eye, in both themes and at three widths |
| behaviour of a server | requests step by step through the scenario, checking what reached the database |
| a rule of the model | the spec, and a run of it through the solver |
| a migration or a move | a run on a copy and a comparison with the source, round by round |
| a document, a report | rereading it against the original requirement: what was promised and what is written |
| an investigation | reproducing the finding from scratch, from your own notes |

**The check is concrete, with sub-items** - a `check` for each. "Needs checking" is not a check, it is a
promise with nothing to close it by. Expectations are written **before** the run, the fact goes into `got`.

## Progress marks

**Tied to events, not to the clock:** a check closed; the marker moved; a check stage passed; new input
arrived. An interval catches the consequences - between two firings there is time to drift into three
tasks.

**A timer is the safety net** for when no event comes for a long time: then the work stalled and nobody
noticed. Every half hour; where the agent supports a scheduled prompt in the session, it is set at the start
of the session. Its text:

> A mark on the way, not the finish and not a request for permission. Take the fact: where the work stands.
> Read the plan (`grep "now=true"`): am I on the right item, what is next, drifted - go back. Extend the
> plan if the part turned out bigger or decisions were said. Mark what is closed. Continue at once.

No long reports: what was done is visible in the work and in the plan. A mark answered with a wall of text
stops being read.

## The utility next to the skill

`plan.mjs` lies here and covers what is done wrong and silently by hand. The plan is still edited by the one
doing the work: there is no command for every edit and there will not be, or half of the operations would
bypass the tool. The plan file comes from `PLAN_FILE`, by default `.claude/session-context/plan.kdl`.

| Command | What for |
| --- | --- |
| `where [N]` | where I am: the current item in full and N next ones as numbers with slugs |
| `check` | the invariants of the items and of their order, and the form of the plan (the limits of the rules above); exit code 1 on violations |
| `take <slug>` | remove the old marker, set the new one, set `since` - three changes at once |
| `done <slug>` | `at` and the marker removed; refused without `done_when` or with checks not passed |
| `add <slug> "<title>" --before/--after <slug>` | add an item **into a gap**: the position from a neighbour, the id a word |

**Inserting into a gap** removes the need to invent a number - that is where `1.1595` comes from. **The tool
sets the time**: a time written by hand once was seven minutes ahead of the clock, and the session showed a
negative length. **Nothing is repaired silently** - a repair would kill the very signal that the marker is
moved the wrong way.

Run it at the start of a session and on every progress mark.

## When to cut

A plan has a limit - a hundred lines and scrolling instead of a glance. So does this skill: **past two
hundred and some lines, cut.** Cut tables and examples, merge places that drifted apart, but **not the
reasons**: a rule without its reason gets reinterpreted.

The one check on the result of a cut: was something that has no column silently dropped - the criterion for
a turn, the event-driven marks, what goes where. That is behaviour, not the state machine.

## Conversation

**A remark is a refinement**, not an order to turn around: add it as an item and continue.

**A turn is different, and the sign is exact:** "stop", "instead", "now", or a remark that cancels the
premise of the current item. Then the marker moves at once. Without this criterion "the plan is kept by the
one doing the work" becomes a reason to postpone a real change.

**Do not turn the work into an audit.** Counting, comparing and writing reports about how everything is
arranged is an imitation. The work is the part that is done: changed code, a written section, a question
closed.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
