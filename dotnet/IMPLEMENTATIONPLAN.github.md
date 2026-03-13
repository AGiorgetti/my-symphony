# Symphony .NET GitHub Implementation Plan (Agent-Executable)

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

Commit rules:

1. Every implementation commit must include a `Co-authored-by:` trailer.
2. Preferred format:
   - `Co-authored-by: <name> <<email>>`
3. If multiple contributors/agents are involved, include one trailer per contributor.

Issue comment rules:

1. Progress comments (including `## Agent Work Log`) must include an attribution line at the end.
2. Preferred format:
   - `Co-authored-by: <name> <<email>>`
3. If exact identity/email is unavailable, use the agreed project alias consistently.

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

## Phase Plan

- Phase 1: Foundation (P0)
- Phase 2: Core Runtime (P0)
- Phase 3: API + Dashboard (P0/P1)
- Phase 4: Adapter Expansion (P1)
- Phase 5: Hardening (P2)

## Epic E1: Solution Foundation

Labels: `type:epic`, `priority:p0`, `area:host`, `area:abstractions`, `area:domain`

### Story E1-S1: Create multi-project solution skeleton

Labels: `type:story`, `priority:p0`, `area:host`
Blocked by: none

Acceptance criteria:

1. Create projects:
   - `Symphony.Host`
   - `Symphony.Application`
   - `Symphony.Domain`
   - `Symphony.Abstractions`
   - `Symphony.Infrastructure`
   - `Symphony.Tracker.GitHub`
   - `Symphony.Tracker.AzureDevOps`
   - `Symphony.Tracker.Linear`
2. Add test projects:
   - `Symphony.Application.Tests`
   - `Symphony.Infrastructure.Tests`
   - `Symphony.Tracker.GitHub.Tests`
   - `Symphony.Tracker.AzureDevOps.Tests`
   - `Symphony.Tracker.Linear.Tests`
   - `Symphony.Host.IntegrationTests`
3. Enforce reference direction from `ARCHITECTURE.md`.
4. Solution builds in Release.

### Story E1-S2: Add DI composition modules

Labels: `type:story`, `priority:p0`, `area:host`, `area:abstractions`
Blocked by: E1-S1

Acceptance criteria:

1. Add DI registration extensions:
   - `AddSymphonyApplication()`
   - `AddSymphonyInfrastructure()`
   - `AddGitHubTrackerAdapter()`
   - `AddAzureDevOpsTrackerAdapter()`
   - `AddLinearTrackerAdapter()`
2. `Program` composes services through `builder.Services` only.
3. Tracker adapter resolved from `tracker.kind`.

### Story E1-S3: Implement core domain models and contracts

Labels: `type:story`, `priority:p0`, `area:domain`, `area:abstractions`
Blocked by: E1-S1

Acceptance criteria:

1. Add domain models aligned with spec (`Issue`, `Workspace`, `RunAttempt`, live session metadata).
2. Add abstraction interfaces for tracker client, workflow loader, workspace manager, process runner, orchestrator control.
3. Add unit tests for domain invariants and validation failures.

## Epic E2: Workflow And Configuration

Labels: `type:epic`, `priority:p0`, `area:infrastructure`, `area:application`

### Story E2-S1: Implement `WORKFLOW.md` front matter loader

Labels: `type:story`, `priority:p0`, `area:infrastructure`
Blocked by: E1-S3

Acceptance criteria:

1. Parse YAML front matter with YamlDotNet.
2. Extract prompt body as template text.
3. Startup fails on invalid workflow with actionable log message.

### Story E2-S2: Implement typed options mapping and validation

Labels: `type:story`, `priority:p0`, `area:application`, `area:infrastructure`
Blocked by: E2-S1

Acceptance criteria:

1. Add typed options for polling, workspace, hooks, agent, codex, tracker.
2. Apply defaults where valid.
3. Validate required fields and reject invalid combinations.

### Story E2-S3: Implement workflow reload with last-known-good fallback

Labels: `type:story`, `priority:p1`, `area:application`, `area:infrastructure`
Blocked by: E2-S2

Acceptance criteria:

1. Runtime reload attempts are supported.
2. Reload failure retains last-known-good config.
3. Reload errors are logged with context.

## Epic E3: Orchestrator Runtime Core

Labels: `type:epic`, `priority:p0`, `area:application`

### Story E3-S1: Implement polling BackgroundService

Labels: `type:story`, `priority:p0`, `area:application`
Blocked by: E2-S2

Acceptance criteria:

1. Poll loop interval comes from typed config.
2. Poll loop supports cancellation and graceful shutdown.
3. Unit tests verify stop behavior and cancellation propagation.

### Story E3-S2: Implement bounded dispatch queue and concurrency gate

Labels: `type:story`, `priority:p0`, `area:application`
Blocked by: E3-S1

Acceptance criteria:

1. Use `System.Threading.Channels` for dispatch queue.
2. Use `SemaphoreSlim` for max concurrency enforcement.
3. Queue and worker state is queryable by status services.

### Story E3-S3: Implement session registry and targeted cancellation

Labels: `type:story`, `priority:p0`, `area:application`
Blocked by: E3-S2

Acceptance criteria:

1. Active sessions tracked by normalized issue id.
2. Terminal/ineligible state transitions cancel matching session only.
3. Session lifecycle transitions are logged with issue/session ids.

### Story E3-S4: Implement retries with Polly v8

Labels: `type:story`, `priority:p0`, `area:application`
Blocked by: E3-S3

Acceptance criteria:

1. Retry pipeline uses exponential backoff and jitter.
2. Retry metadata persisted in runtime state.
3. Non-transient failures exit retry loop correctly.

## Epic E4: Workspace And Execution Safety

Labels: `type:epic`, `priority:p0`, `area:infrastructure`

### Story E4-S1: Implement workspace manager with root safety constraints

Labels: `type:story`, `priority:p0`, `area:infrastructure`
Blocked by: E2-S2

Acceptance criteria:

1. Workspace path generation is deterministic and sanitized.
2. Path traversal/out-of-root resolution is rejected.
3. Cleanup behavior for terminal issues matches spec.

### Story E4-S2: Implement process runner for CLI invocation standard

Labels: `type:story`, `priority:p0`, `area:infrastructure`
Blocked by: E1-S3

Acceptance criteria:

1. Use `ProcessStartInfo` with `UseShellExecute=false`, redirected output, explicit working directory, argument list entries.
2. Capture stdout/stderr/exit code/duration.
3. Respect cancellation and terminate process on cancellation.

### Story E4-S3: Implement Codex agent runner

Labels: `type:story`, `priority:p0`, `area:application`, `area:infrastructure`
Blocked by: E4-S1, E4-S2, E3-S3

Acceptance criteria:

1. Codex command and args sourced from typed config.
2. Execution occurs only within validated workspace path.
3. Run attempt status transitions update orchestration state.

## Epic E5: Tracker Adapter MVP (GitHub First)

Labels: `type:epic`, `priority:p0`, `area:tracker-github`

### Story E5-S1: Implement GitHub tracker adapter

Labels: `type:story`, `priority:p0`, `area:tracker-github`
Blocked by: E1-S3, E2-S2

Acceptance criteria:

1. Read candidate issues from configured repository and active states.
2. Normalize issue payload to domain model.
3. Adapter tests cover auth failure, empty results, paging.

### Story E5-S2: Integrate adapter with orchestrator runtime

Labels: `type:story`, `priority:p0`, `area:tracker-github`, `area:application`
Blocked by: E5-S1, E3-S4, E4-S3

Acceptance criteria:

1. Poll loop uses selected adapter.
2. Eligible issues dispatch into execution queue.
3. State transition reconciliation stops/cancels invalidated work.

## Epic E6: API v1 Surface

Labels: `type:epic`, `priority:p0`, `area:api`, `area:host`

### Story E6-S1: Implement API route group and DTO contracts

Labels: `type:story`, `priority:p0`, `area:api`
Blocked by: E3-S3

Acceptance criteria:

1. Implement endpoints:
   - `GET /api/v1/health`
   - `GET /api/v1/issues`
   - `GET /api/v1/issues/{id}`
   - `GET /api/v1/sessions`
   - `GET /api/v1/sessions/{issueId}`
   - `POST /api/v1/sessions/{issueId}/cancel`
   - `POST /api/v1/orchestrator/pause`
   - `POST /api/v1/orchestrator/resume`
   - `GET /api/v1/metrics/summary`
2. Endpoints are thin and delegate to application services.
3. Integration tests verify happy-path and error responses.

## Epic E7: Blazor Dashboard

Labels: `type:epic`, `priority:p0`, `area:dashboard`, `area:host`

### Story E7-S1: Implement dashboard shell at `/`

Labels: `type:story`, `priority:p0`, `area:dashboard`
Blocked by: E6-S1

Acceptance criteria:

1. Root page loads Blazor Server dashboard.
2. Shows service health, orchestrator mode, last poll tick.
3. Does not block orchestrator operation if UI fails.

### Story E7-S2: Implement operational panels

Labels: `type:story`, `priority:p1`, `area:dashboard`
Blocked by: E7-S1

Acceptance criteria:

1. Active sessions panel.
2. Retry queue panel with next retry ETA.
3. Recent attempts panel with outcomes and error summary.

## Epic E8: Observability, Tests, And Hardening

Labels: `type:epic`, `priority:p1`, `area:observability`, `area:tests`

### Story E8-S1: Structured logging baseline with Microsoft.Extensions.Logging

Labels: `type:story`, `priority:p0`, `area:observability`
Blocked by: E3-S1

Acceptance criteria:

1. Include issue/session identifiers in orchestration and execution events.
2. Log startup, poll tick, dispatch, cancel, retry, completion paths.
3. Support JSON console formatting configuration.

### Story E8-S2: Host integration and end-to-end smoke tests

Labels: `type:story`, `priority:p1`, `area:tests`
Blocked by: E6-S1, E7-S1

Acceptance criteria:

1. Integration tests cover API contracts and startup behavior.
2. Smoke scenario validates workflow load -> issue poll -> workspace create -> agent run attempt.
3. Tests run in CI on PRs.

### Story E8-S3: Add Azure DevOps and Linear adapters

Labels: `type:story`, `priority:p1`, `area:tracker-azure-devops`, `area:tracker-linear`
Blocked by: E5-S2

Acceptance criteria:

1. Azure DevOps adapter implemented and tested against contract.
2. Linear adapter implemented and tested against contract.
3. Host selects either adapter from `tracker.kind` with no application changes.

### Story E8-S4: Reliability hardening and operational safeguards

Labels: `type:story`, `priority:p2`, `area:application`, `area:infrastructure`
Blocked by: E8-S2

Acceptance criteria:

1. Add degraded health semantics (last successful poll age, workflow load status).
2. Improve cancellation/timeout diagnostics.
3. Add extra safety tests around workspace path constraints.

## Agent Execution Order

Use this order for autonomous execution:

1. E1-S1 -> E1-S2 -> E1-S3
2. E2-S1 -> E2-S2
3. E3-S1 -> E3-S2 -> E3-S3 -> E3-S4
4. E4-S1 -> E4-S2 -> E4-S3
5. E5-S1 -> E5-S2
6. E6-S1
7. E7-S1 -> E7-S2
8. E8-S1 -> E8-S2 -> E8-S3 -> E8-S4
9. E2-S3 can be scheduled after E2-S2 or after MVP stabilization.

## Suggested GitHub Milestone Mapping

- `MVP-P0`: E1, E2-S1/S2, E3, E4, E5, E6-S1, E7-S1, E8-S1
- `MVP-P1`: E2-S3, E7-S2, E8-S2, E8-S3
- `Hardening-P2`: E8-S4

## Suggested Issue Template For Stories

Copy this into each GitHub story issue body:

```md
## Objective

<one clear sentence>

## Scope

- In scope:
- Out of scope:

## Acceptance Criteria

1.
2.
3.

## Dependencies

- blocked-by: #<issue>

## Agent Work Log

- Keep a single comment titled: ## Agent Work Log
- Update it with: Current Step, Completed, Next Action, Changed Files, Validation Evidence, Blockers
- End each update with: Co-authored-by: <name> <<email>>

## Validation

- [ ] dotnet format --verify-no-changes
- [ ] dotnet build -c Release
- [ ] dotnet test -c Release

## Docs

- [ ] Updated docs if behavior/config changed
```
