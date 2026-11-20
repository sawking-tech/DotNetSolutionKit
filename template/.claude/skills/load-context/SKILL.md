---
name: load-context
description: Load the state of the session at its start - the tasks in work from .claude/session-context/active.md, the standing rules from common.md, then the details of the one task the user picks. Ask what we do before touching anything.
---

Load the state of the session, then ask what we do.

The state lives in `.claude/session-context/`, which `.gitignore` keeps out of the repository. It is
written by `/save-context` at the end of every session, so a session that starts right after one finds it
current. Read it in small pieces: the table of tasks first, the details of one task only after the user
picks it.

## What to do

1. Read `.claude/session-context/active.md` - the list of tasks in work, one line each. Read nothing deeper
   at this step.
2. Read `.claude/session-context/common.md` if it is there - the rules that hold while the current line of
   work is alive, and what has no home in a single task. Drop a rule that has lived out its line of work;
   a permanent rule belongs in the rules file of the solution, not here.
3. Ask the user which task we take, naming the tasks of the list and offering a new one. One question, not
   one per task.
4. Read `.claude/session-context/current/<slug>.md` of the task picked - the progress in prose, what was
   decided, where the last session stopped.
5. Say in one sentence what is in work and where it stopped, then start.

## When there is nothing to load

No `active.md` means the solution has no tasks recorded yet, not that the work is lost: ask the user what
we do, and go on from there. A session that ends with something to come back to closes with
`/save-context`; work too long for one session follows `/plan-and-iterate`, whose plan lives in the same
folder.

Never invent a task the file does not name and never reconstruct progress from memory of a previous
session: the file is the record, the session's memory of it is a hypothesis.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
