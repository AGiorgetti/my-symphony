# Agent Instructions — Symphony GitHub Issue Agent

Status: Implementation plan for GitHub issue tracking

## Purpose

This backlog is designed for autonomous or semi-autonomous coding agents working in GitHub.
It decomposes the implementation into dependency-ordered issues with clear acceptance criteria,
testing expectations, and definition of done.

This plan follows:

- `../SPEC.md`
- `./AGENTS.md`
- `./ARCHITECTURE.md`

## How To Use In GitHub

1. Create labels listed in the Label Set section.
2. Create one milestone per phase (`MVP-P0`, `MVP-P1`, `Hardening-P2`).
3. Create epic issues first, then story issues under each epic.
4. Add `blocked-by` references from each issue to its prerequisites.
5. Assign issues to agent runs in dependency order.

## Label Set

- `area:host`
- `area:application`
- `area:domain`
- `area:abstractions`
- `area:infrastructure`
- `area:tracker-github`
- `area:tracker-azure-devops`
- `area:tracker-linear`
- `area:api`
- `area:dashboard`
- `area:observability`
- `area:tests`
- `type:epic`
- `type:story`
- `priority:p0`
- `priority:p1`
- `priority:p2`
- `status:blocked`
- `status:ready`
- `status:in-progress`

## Issue Lifecycle State Machine

Each story issue should follow this lifecycle:

1. `status:ready`
2. `status:in-progress`
3. Pull request open and linked
4. Pull request merged with validation evidence
5. Issue closed

Rules:

1. An agent may start only issues in `status:ready` with all dependencies closed.
2. Starting work must switch label to `status:in-progress`.
3. Closing an issue requires all acceptance and validation checkboxes checked.
4. If interrupted, keep issue open in `status:in-progress` and follow the resume protocol below.

## Completion Tracking Protocol

Completion is tracked through issue checklists plus verifiable artifacts.

Required artifacts per story:

1. Acceptance Criteria checklist completed in the issue body.
2. Validation checklist completed in the issue body.
3. Linked PR containing implementation.
4. PR validation section with command outcomes.
5. Merge commit closing the issue.
6. Issue progress updates recorded in the `Agent Work Log` comment.

The issue is not complete until all six artifacts exist.

## Stop And Resume Protocol (Agent Handoff-Safe)

Every story issue must contain a single comment titled:

`## Agent Work Log`

The active agent updates this same comment throughout execution.

Required sections in the work log comment:

1. `Current Step`
2. `Completed`
3. `Next Action`
4. `Changed Files`
5. `Validation Evidence`
6. `Blockers`

Update rules:

1. Before starting code changes, create or refresh the work log comment.
2. After each meaningful milestone, update the same comment instead of posting new status comments.
3. Before ending a run, set `Next Action` to a concrete single action another agent can execute immediately.
4. If blocked, include exact blocker details and mark issue `status:blocked`.
5. Each run must leave at least one progress update in the issue (`Started`, `Progress`, `Blocked`, or `Completed`).

Resume rules for a new agent:

1. Read issue body, dependencies, and the latest `Agent Work Log` comment.
2. Verify branch and PR state.
3. Re-run validation steps only as needed to re-establish confidence.
4. Continue from `Next Action` and update the same comment.

This protocol ensures work is resumable even when runs stop unexpectedly.

## Co-Authorship And Attribution Policy

All implementation activity must keep explicit authorship attribution.

Author/co-author convention:

1. Author must be the currently logged-in GitHub user.
2. Co-author must be the agent:
   - `Co-authored-by: name <email>`

Commit rules:

1. Every implementation commit must include a `Co-authored-by:` trailer.
2. Preferred format:
   - `Co-authored-by: name <email>`
3. If multiple contributors/agents are involved, include one trailer per contributor.

Issue comment rules:

1. Progress comments (including `## Agent Work Log`) must include an attribution line at the end.
2. Preferred format:
   - `Co-authored-by: name <email>`
3. If exact identity/email is unavailable, use the agreed project alias consistently.

Allowlist and identity management

- Keep a canonical allowlist of agent display names and emails in `.github/agent-authors.yml`. Agents should pick an identity from that list when adding trailers.

## Global Definition Of Done

Every story must satisfy all items below:

1. Acceptance criteria are implemented and verified.
2. Appropriate tests are added or updated (unit/integration/e2e as applicable).
3. Structured logging is present for new critical flows.
4. No `.Result` or `.Wait()` introduced in async paths.
5. DI registration is done through `builder.Services` only.
6. Docs updated when behavior/config contract changes (`SPEC.md`, `ARCHITECTURE.md`, `README.md`, `AGENTS.md` as needed).
7. Validation commands executed from `dotnet/`:
   - `dotnet format --verify-no-changes`
   - `dotnet build -c Release`
   - `dotnet test -c Release`
8. Issue progress has been updated during execution in the `Agent Work Log`.
9. Commits and progress comments include co-authorship attribution.

## PR Requirements

- PR body must follow `../.github/pull_request_template.md`.
- Include a concise validation section listing the exact commands run and outcomes.
