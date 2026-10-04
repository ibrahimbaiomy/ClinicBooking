# CLAUDE.md

Before writing any code in this repository:

1. Read `docs/Instructions.md` in full (rules, conventions, structure, build commands).
2. Read the index at the top of `docs/decisions.md`.
3. Read in full every decision marked `*` in the index, every decision whose
   keywords match the task, and every decision those reference. When unsure
   whether a decision is relevant, read it. Read an `OPEN` question in full when
   the task depends on it.
4. In your plan, list the decision numbers you consulted, so coverage can be checked.

The non-negotiable rules in `docs/Instructions.md` always apply. If a task
conflicts with them, or depends on an `OPEN` question, stop and say so instead of
guessing.

Never run `docker` or `docker compose` commands; the owner runs Docker. Report
Docker-dependent checks as "by-hand checks for the owner" (see "Workflow
expectations" in `docs/Instructions.md`).
