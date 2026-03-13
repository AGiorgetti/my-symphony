# Symphony .NET

This directory contains the .NET 10 ASP.NET Core implementation of Symphony: an orchestration service that polls Linear, creates per-issue workspaces, and runs Codex in app-server mode.

## Environment

- .NET SDK: `10.0.x`.
- ASP.NET Core target framework: `net10.0`.
- Restore dependencies: `dotnet restore`.
- Build: `dotnet build -c Release`.

## Codebase-Specific Conventions

- Runtime configuration is sourced from `WORKFLOW.md` front matter and mapped through typed options.
- Keep the implementation aligned with `../SPEC.md` where practical.
  - The implementation may be a superset of the spec.
  - The implementation must not conflict with the spec.
  - If implementation changes meaningfully alter intended behavior, update the spec in the same change where practical.
- Prefer options/config abstractions over ad-hoc environment variable reads.
- Workspace safety is critical:
  - Never run Codex with turn cwd in the source repository.
  - Workspaces must stay under the configured workspace root.
- Orchestrator behavior is stateful and concurrency-sensitive; preserve retry, reconciliation, and cleanup semantics.
- Follow existing structured logging conventions and always include issue/session identifiers in logs.

## ASP.NET Core Guidance

- Use minimal APIs for orchestration endpoints unless the surrounding code already uses MVC controllers.
- Register dependencies through DI (`builder.Services`) and avoid static mutable state.
- Use `IHostedService`/`BackgroundService` for polling and long-running orchestration loops.
- Keep HTTP endpoints thin; place orchestration logic in services.
- Propagate cancellation tokens through I/O boundaries and long-running operations.

## Tests and Validation

Run targeted tests while iterating, then execute full validation before handoff.

```bash
dotnet test -c Release
```

Recommended pre-handoff checks:

```bash
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
```

## Required Rules

- Keep changes narrowly scoped; avoid unrelated refactors.
- Follow existing naming and folder patterns in this directory.
- Public APIs should be explicitly documented where behavior is non-obvious.
- For async code, avoid `.Result`/`.Wait()` and prefer `async`/`await` end-to-end.

## PR Requirements

- PR body must follow `../.github/pull_request_template.md`.
- Include a concise validation section listing the exact commands run and outcomes.

## Docs Update Policy

If behavior/config changes, update docs in the same PR:

- `../README.md` for project concept and goals.
- `README.md` for .NET implementation and run instructions.
- `WORKFLOW.md` for workflow/config contract changes.
