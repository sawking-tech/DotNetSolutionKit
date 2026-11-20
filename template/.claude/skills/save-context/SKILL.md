---
name: save-context
description: Save the state of the session to .claude/session-context/ - active.md as the list of tasks, common.md for the standing rules, current/{slug}.md for the task's progress in prose. Run at the end of a session and before a long pause.
---

Save the state of the session, so the next one starts from it rather than from nothing.

The state lives in `.claude/session-context/`, which `.gitignore` keeps out of the repository. Write every
file from what the session actually did - not from what it was asked to do and did not finish.

```
.claude/session-context/
  active.md      - the list of tasks in work: a line per task, status, where its details are
  common.md      - the rules that hold while the current line of work is alive
  current/       - a file per task: current/{slug}.md
  facts/         - one fact a file, whatever outlives the task
  digest/        - the summary of a discussion worth keeping
  raw/           - data taken verbatim: a command's output, a URL
```

## What to do

1. **Check the state against reality, before writing a word.** `git status -sb` and the last commits name
   what is done, what is uncommitted and on which branch; a pull request, if the session opened or updated
   one. A file that says a task is finished while the work sits uncommitted is the drift this step stops.
2. **Decide the status of each task** that the session touched: `in_progress`, `ready_for_merge`, `done`,
   `blocked` or `frozen`. A task that reached `done` leaves `active.md` for the completed list.
3. **Write `current/{slug}.md`** for every task whose state moved. Progress in prose: what is done (a
   commit hash where it matters) and where the session stopped. Decisions and findings that a later
   session would need again. Not the plan of the next steps - that stays in the plan file, and a copy of it
   here is a duplicate that goes stale.
4. **Write `active.md`**: the list of tasks in work with their status and the path to their detail file;
   then what is completed in the last week, a line each. A task's line and its `status` in
   `current/{slug}.md` must say the same thing.
5. **Write `common.md`** only when the rules changed. A temporary rule whose line of work is over goes
   out; a rule that holds always belongs in the rules file of the solution.
6. Keep one file per subject: a fact found on the way goes to `facts/`, a discussion's conclusion to
   `digest/`, data taken verbatim to `raw/`. Do not append an "Update - <date>" section to a file that
   already has one; rewrite the file so it reads as one state, not as a stack of turns.

## The file of a task

```markdown
---
title: {slug}
status: in_progress | ready_for_merge | done | blocked | frozen
branch: feature/...
pr: 201
last_synced: YYYY-MM-DD HH:MM
---

# {slug} - a short heading

What the task is and why, in two or three sentences.

Progress in prose: what is done by now, what turned up, where the session stopped.
```

The frontmatter is the only fixed part; the body is a text a person reads, so write it as one. The time in
`last_synced` comes from the clock, never from memory.

## What does not go here

- **The plan.** The next steps of work too long for one session live in the plan file of
  `/plan-and-iterate`, in the same folder. Here is the state: what is done and where we stand.
- **A secret.** This folder is kept out of git, but a token pasted into `raw/` still leaves the machine in
  a copy of the folder; a value that must survive belongs in the solution's secret store.
- **A guess.** A claim about the code that the session did not check is a hypothesis; say what was checked
  and how, or leave it out.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
