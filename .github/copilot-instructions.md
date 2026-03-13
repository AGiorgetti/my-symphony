# Copilot Instructions — my-symphony

## What this repo is

A customized fork of [Symphony](https://github.com/openai/symphony).
Symphony is a long-running orchestration service that polls an issue tracker, creates per-issue
git worktrees, and runs a coding agent inside each workspace until the issue reaches a
terminal or handoff state.

Canonical spec: [`SPEC.md`](../SPEC.md)  
.NET implementation guide: [`dotnet/README.md`](../dotnet/README.md)  
.NET agent conventions: [`dotnet/AGENTS.md`](../dotnet/AGENTS.md)

---

## Build and test (dotnet/)

All commands run from the `dotnet/` directory.

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

**Pre-handoff gate** (must pass before pushing):

```bash
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
```

Workspace bootstrap script (restores dependencies in a fresh worktree):

```bash
.codex/worktree_init.sh
```

---

## Key architectural rules

- **Workspace safety**: Codex must never run with `cwd` pointing at the source repository.
  Workspaces must stay under the configured workspace root.
- **Spec contract**: Implementation may extend but must not conflict with `SPEC.md`.
  Behavioral changes must update `SPEC.md` in the same PR.
- **Config sourcing**: Runtime configuration lives in `WORKFLOW.md` front matter (YAML) and is
  mapped through typed options classes — avoid ad-hoc env reads.
- **Async discipline**: No `.Result`/`.Wait()`; propagate `CancellationToken` through I/O.
- **Concurrency**: Orchestrator state is stateful and concurrency-sensitive; preserve retry,
  reconciliation, and cleanup semantics.
- **Structured logging**: Follow existing logging conventions; always include issue/session IDs.
- **Minimal APIs**: Use ASP.NET Core minimal APIs unless surrounding code uses MVC controllers.
- **DI only**: Register via `builder.Services`; no static mutable state.

---

## Tracker adapters

Symphony supports pluggable issue tracker adapters. Each has a companion workflow template:

| Adapter       | Env var             | Workflow template                         |
|--------------|---------------------|-------------------------------------------|
| `github`      | `GITHUB_TOKEN`      | [`WORKFLOW.github.md`](../dotnet/WORKFLOW.github.md) |
| `azure_devops`| `AZURE_DEVOPS_PAT`  | [`WORKFLOW.azure-devops.md`](../dotnet/WORKFLOW.azure-devops.md) |
| `linear`      | `LINEAR_API_KEY`    | [`WORKFLOW.linear.md`](../dotnet/WORKFLOW.linear.md) |

---

## PR requirements

- PR body must follow [`.github/pull_request_template.md`](pull_request_template.md) — fill every
  section, replace all placeholder comments.
- Include a validation section listing exact commands run and their outcomes.
- Keep changes narrowly scoped; avoid unrelated refactors.
- If behavior or config changes, update docs in the same PR (see
  [dotnet/AGENTS.md § Docs Update Policy](../dotnet/AGENTS.md)).

---

## Available skills

Skills are invocable workflows for common agent tasks. Load the skill file before following it.

| Skill                | When to use                                              | File |
|---------------------|----------------------------------------------------------|------|
| `commit`            | Produce a well-formed git commit                         | [`.agents/skills/commit/SKILL.md`](../.agents/skills/commit/SKILL.md) |
| `push`              | Push branch and create/update a PR                      | [`.agents/skills/push/SKILL.md`](../.agents/skills/push/SKILL.md) |
| `pull`              | Sync feature branch with `origin/main`                  | [`.agents/skills/pull/SKILL.md`](../.agents/skills/pull/SKILL.md) |
| `land`              | Monitor, resolve conflicts, and squash-merge a PR       | [`.agents/skills/land/SKILL.md`](../.agents/skills/land/SKILL.md) |
| `linear`            | Raw Linear GraphQL operations                           | [`.agents/skills/linear/SKILL.md`](../.agents/skills/linear/SKILL.md) |
| `harness-engineering` | Improve agent legibility and harness structure        | [`.agents/skills/harness-engineering/SKILL.md`](../.agents/skills/harness-engineering/SKILL.md) |

---

## CI

| Workflow | Triggers | What it does |
|----------|----------|--------------|
| `make-all.yml` | PR, push to `main` | format check → build → test |
| `pr-description-lint.yml` | PR | validates PR body against template |
