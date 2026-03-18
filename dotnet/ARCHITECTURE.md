# Symphony .NET Architecture Decisions

Status: Architectural guideline for implementation planning

## Purpose

This document defines the implementation architecture for the .NET Symphony service.
It captures decisions agreed during design so coding can proceed with minimal ambiguity.

This document is guidance, not a strict guarantee of the current runtime state. The implementation may diverge temporarily as work progresses.

Alignment policy:

- Keep this document aligned with [../SPEC.md](../SPEC.md) and the implemented behavior whenever practical.
- If implementation meaningfully changes behavior, update this document and [../SPEC.md](../SPEC.md) in the same change when possible.
- If immediate sync is not feasible, treat documentation drift as technical debt and reconcile it in the next practical change.

## Decision Summary

1. Host and runtime model
- Use plain ASP.NET Core on .NET 10 with Generic Host.
- Use a long-running orchestrator implemented as `BackgroundService`.
- Do not use Orleans or Akka.NET in v1.
- The application and its execution paths must remain cross-platform across Windows, Linux, and macOS.

2. Concurrency and orchestration
- Use `System.Threading.Channels` to decouple polling and execution dispatch.
- Use `SemaphoreSlim` to enforce `agent.max_concurrent_agents`.
- Keep authoritative orchestration state in-memory with restart recovery via reconciliation, as defined by spec.

3. Logging and observability
- Use `Microsoft.Extensions.Logging` only (no Serilog dependency in baseline).
- Prefer structured logs with scopes and message templates.
- Include issue/session identifiers in all orchestration and execution logs.

4. External integrations
- Prefer CLI invocation over library SDKs when interacting with external tools/services in execution paths.
- Use `System.Diagnostics.Process` with explicit argument list and redirected stdout/stderr.
- Process launch behavior must support Windows, Linux, and macOS without assuming a Unix-only shell environment.
- Never run shell-interpolated commands with untrusted values.

5. Dashboard and API
- Provide an operator dashboard using Blazor Server.
- Provide machine-consumable Minimal API endpoints under `/api/v1`.
- Dashboard and API run in the same ASP.NET Core process.

6. Configuration and templates
- Load workflow settings from `WORKFLOW.md` YAML front matter.
- Parse YAML with YamlDotNet.
- Render workflow prompt body with Scriban.

7. Resilience and retries
- Use Polly v8 resilience pipelines for retry and backoff.
- Retry only transient failures; respect cancellation on state transitions and shutdown.

8. Dependency injection
- Use the built-in .NET dependency injection container (`Microsoft.Extensions.DependencyInjection`).
- Register all services through `builder.Services` with explicit lifetimes.
- Do not introduce service locator or static mutable singletons.

## Non-Goals for v1

- Distributed actor runtime (Orleans/Akka.NET).
- Persistent orchestrator database.
- Multi-node scheduling and leader election.
- Separate frontend deployment for dashboard.

## Why Plain ASP.NET Core

This project is an orchestration daemon with optional web surface, not a distributed actor platform.
Plain ASP.NET Core provides:

- Native hosting primitives (`BackgroundService`, DI, options, health checks).
- Minimal complexity and operational overhead.
- Tight alignment with current repository conventions in [AGENTS.md](./AGENTS.md).

## Dependency Injection Standard

Use the framework DI container as the single composition mechanism.

Rules:

- Composition root is `Program.cs` only.
- Prefer constructor injection; avoid resolving services manually from `IServiceProvider` except at composition boundaries.
- Use `Singleton` for stateless coordinators and shared registries that are intentionally process-wide.
- Use `Scoped` for request-bound API services when needed.
- Use `Transient` for lightweight stateless helpers that do not hold shared state.
- Expose interfaces at component boundaries (`IOrchestratorControl`, `IIssueTrackerClient`, `IProcessRunner`, etc.).
- Keep options binding strongly typed via `IOptions<T>`/`IOptionsMonitor<T>`.

## High-Level Architecture

1. Workflow Loader
- Reads and parses `WORKFLOW.md` into raw config plus prompt template.

2. Configuration Layer
- Maps parsed config into typed options with validation.

3. Issue Tracker Adapter
- Fetches and normalizes issues from configured tracker kind.

4. Orchestrator
- Poll loop, eligibility decisions, dispatching, cancellation, retries, reconciliation.

5. Workspace Manager
- Creates and validates per-issue workspaces under configured root.

6. Agent Runner
- Invokes codex app-server command in workspace and streams lifecycle events.

7. Status Surfaces
- Blazor dashboard for operators.
- Minimal API for automation and integrations.

8. Logging
- Structured logs for all major transitions and outcomes.

## Solution Structure Decision

Yes: split into multiple projects.

Reasoning:

- Pluggable adapters require stable contracts and isolated implementation dependencies.
- Orchestration logic should stay independent from tracker-specific APIs and payload models.
- Separate test projects can validate each adapter against its contract without coupling to host concerns.

Recommended solution layout:

- `dotnet/src/Symphony.Host`
	- ASP.NET Core host, Minimal APIs, Blazor Server dashboard, DI composition root.
- `dotnet/src/Symphony.Application`
	- Orchestration use-cases, policies, session lifecycle, retry coordination.
- `dotnet/src/Symphony.Domain`
	- Core domain models and invariants (`Issue`, `RunAttempt`, session state, value objects).
- `dotnet/src/Symphony.Abstractions`
	- Contracts/interfaces for trackers, workspace manager, process runner, clock, and event publishing.
- `dotnet/src/Symphony.Infrastructure`
	- Shared technical infrastructure: process execution, filesystem/workspace services, YAML loader, template renderer.
- `dotnet/src/Symphony.Tracker.GitHub`
	- GitHub adapter implementation behind `IIssueTrackerClient`.
- `dotnet/src/Symphony.Tracker.AzureDevOps`
	- Azure DevOps adapter implementation behind `IIssueTrackerClient`.
- `dotnet/src/Symphony.Tracker.Linear`
	- Linear adapter implementation behind `IIssueTrackerClient`.

Recommended tests:

- `dotnet/tests/Symphony.Application.Tests`
- `dotnet/tests/Symphony.Infrastructure.Tests`
- `dotnet/tests/Symphony.Tracker.GitHub.Tests`
- `dotnet/tests/Symphony.Tracker.AzureDevOps.Tests`
- `dotnet/tests/Symphony.Tracker.Linear.Tests`
- `dotnet/tests/Symphony.Host.IntegrationTests`

Dependency direction (must hold):

- `Host` -> `Application` -> (`Domain`, `Abstractions`)
- `Infrastructure` -> (`Application`, `Abstractions`, `Domain`)
- `Tracker.*` -> `Abstractions` (+ `Domain` mapping models if needed)
- `Domain` must not depend on infrastructure or host projects.
- Tracker projects must not be referenced by `Application`; selection is done in `Host` via DI/config.

## Process Execution Standard (CLI-First)

All tool and service interactions that are CLI-capable follow these requirements:

1. Use `ProcessStartInfo` with:
- `UseShellExecute = false`
- Redirected stdout/stderr
- Explicit `WorkingDirectory`
- Argument list entries added one by one

2. Propagate `CancellationToken` to process wait and read operations.

3. Treat non-zero exit codes as failures and capture stderr in logs.

4. Never build a single shell command string from issue/workflow/user values.

5. Choose process launch strategy and shell integration in a platform-aware way so the same workflow can execute on Windows, Linux, and macOS.

## Concurrency and Session Model

- Maintain an in-memory dictionary keyed by normalized issue ID for active sessions.
- Each active session owns a `CancellationTokenSource`.
- State changes to terminal/ineligible states trigger targeted cancellation.
- Polling and dispatch are decoupled through a bounded channel to avoid burst overload.
- Retries are tracked per issue with exponential backoff and jitter.

## Project Registration and Pluggability

Pluggable adapters are loaded by composition in `Symphony.Host`.

Registration pattern:

- `tracker.kind` in `WORKFLOW.md` drives adapter selection.
- Host registers exactly one `IIssueTrackerClient` implementation at runtime.
- Adapter-specific options are bound and validated only when that adapter is selected.

Suggested DI extension methods:

- `services.AddSymphonyApplication()`
- `services.AddSymphonyInfrastructure()`
- `services.AddGitHubTrackerAdapter()`
- `services.AddAzureDevOpsTrackerAdapter()`
- `services.AddLinearTrackerAdapter()`

This keeps the host clean and makes adding a new tracker adapter a new project plus one composition branch, without touching orchestration logic.

## API Contract Baseline

Route group: `/api/v1`

Required endpoints:

- `GET /health`
- `GET /issues`
- `GET /issues/{id}`
- `GET /sessions`
- `GET /sessions/{issueId}`
- `POST /sessions/{issueId}/cancel`
- `POST /orchestrator/pause`
- `POST /orchestrator/resume`
- `GET /metrics/summary`

Conventions:

- JSON responses with stable DTOs.
- Thin endpoints delegating to orchestration services.
- Use typed results and explicit status codes.

## Dashboard Baseline

Blazor Server dashboard pages/components:

- System health and last successful poll tick.
- Active sessions with issue ID, state, attempt, start time.
- Retry queue snapshot with next retry ETA.
- Recent run attempts and outcomes.
- Current orchestrator mode (running/paused).

Dashboard data should be sourced from the same internal services used by the API.

## Dependency Baseline

Expected core packages:

- `YamlDotNet`
- `Scriban`
- `Polly.Core` (or Polly v8 equivalent packages)

Use built-in framework features where possible:

- `Microsoft.Extensions.Logging`
- `Microsoft.Extensions.Options`
- `System.Threading.Channels`
- `System.Text.Json`
- ASP.NET Core Minimal APIs
- Blazor Server

## Security and Safety Baseline

- Enforce workspace root constraints before running any process.
- Reject execution if computed workspace path is outside configured root.
- Validate all runtime configuration at startup.
- On reload failures, keep last known good configuration and log error.
- Pass only required environment values to child processes.

## Testing Strategy Baseline

1. Unit tests
- Workflow parsing and typed option validation.
- Issue eligibility and dispatch ordering logic.
- Retry policy behavior and cancellation semantics.

2. Integration tests
- Minimal API endpoints.
- Process runner behavior with mocked process boundaries where practical.
- Tracker adapter contract tests with canned payloads.

3. End-to-end smoke
- Start host with test workflow.
- Simulate active issue and verify workspace creation, agent spawn, and status surface updates.

## Future Evolution (Explicitly Deferred)

- Persistent state store for fast warm recovery.
- Multi-host coordination and sharding.
- Role-based access control for operator endpoints.
- Dedicated web frontend separation if UI complexity grows.
