# Symphony .NET

This directory documents the ASP.NET Core implementation of Symphony, based on
[`SPEC.md`](../SPEC.md) at the repository root.

> [!WARNING]
> Symphony .NET is prototype software intended for evaluation in trusted environments and is
> presented as-is.

## How it works

1. Polls a configured issue tracker adapter for candidate work.
2. Creates a workspace per issue.
3. Launches Codex in [App Server mode](https://developers.openai.com/codex/app-server/) inside the
   workspace.
4. Sends a workflow prompt to Codex.
5. Keeps Codex working on the issue until the issue reaches a terminal or handoff state.

If a claimed issue moves to a terminal state, Symphony stops the active agent for that issue and
cleans up matching workspaces.

## Issue tracker model (pluggable)

Per [`SPEC.md`](../SPEC.md), Symphony uses pluggable issue tracker adapters selected by
`tracker.kind`.

Supported kinds:

- `github`
- `azure_devops`
- Optional: `linear`

Built-in workflow templates in this folder:

- [`WORKFLOW.github.md`](./WORKFLOW.github.md)
- [`WORKFLOW.azure-devops.md`](./WORKFLOW.azure-devops.md)
- [`WORKFLOW.linear.md`](./WORKFLOW.linear.md)

Tracker configuration fields vary by adapter:

- Shared: `tracker.kind`, `tracker.endpoint`, `tracker.api_key`, `tracker.active_states`,
  `tracker.terminal_states`
- GitHub: `tracker.repository` (`owner/repo`)
- Azure DevOps: `tracker.organization`, `tracker.project`
- Linear: `tracker.project_slug`

Canonical API key environment variables:

- `GITHUB_TOKEN` for `github`
- `AZURE_DEVOPS_PAT` for `azure_devops`
- `LINEAR_API_KEY` for `linear`

## Tracker-specific notes

### GitHub notes

- Use `tracker.kind: github`.
- Required field: `tracker.repository` in `owner/repo` format.
- Default endpoint: `https://api.github.com`.
- Canonical auth variable: `GITHUB_TOKEN`.
- Typical state mapping:
  - `tracker.active_states`: `open`
  - `tracker.terminal_states`: `closed`
- GitHub issue state is coarse (`open`/`closed`), so workflow stages are typically modeled with
  labels while issue state remains `open`.
  - Common label stages in the provided workflow: `backlog`, `todo`, `in-progress`,
    `human-review`, `merging`, `rework`, `done`.
- Start from [`WORKFLOW.github.md`](./WORKFLOW.github.md) and adapt labels/status flow for your
  repo process.

GitHub front matter example:

```yaml
tracker:
  kind: github
  endpoint: https://api.github.com
  api_key: $GITHUB_TOKEN
  repository: "OWNER/REPO"
  active_states:
    - open
  terminal_states:
    - closed
```

### Azure DevOps notes

- Use `tracker.kind: azure_devops`.
- Required fields: `tracker.organization`, `tracker.project`.
- Default endpoint: `https://dev.azure.com`.
- Canonical auth variable: `AZURE_DEVOPS_PAT`.
- Azure state names vary by process template, so set `tracker.active_states` and
  `tracker.terminal_states` explicitly for your project.
- Typical mapping in the provided workflow:
  - Active states: `New`, `Active`, `Committed`, `Rework`
  - Terminal states: `Closed`, `Done`, `Removed`
- Logical stage mapping often used with this template:
  - `New/Backlog` for queued work
  - `Active` for implementation
  - `Committed` for human review
  - Team-defined merge-ready state for landing
  - `Rework` for requested changes
- Start from [`WORKFLOW.azure-devops.md`](./WORKFLOW.azure-devops.md) and adapt state names to your
  board workflow.

Azure DevOps front matter example:

```yaml
tracker:
  kind: azure_devops
  endpoint: https://dev.azure.com
  api_key: $AZURE_DEVOPS_PAT
  organization: "YOUR_ORG"
  project: "YOUR_PROJECT"
  active_states:
    - New
    - Active
    - Committed
    - Rework
  terminal_states:
    - Closed
    - Done
    - Removed
```

### Linear notes

- Use `tracker.kind: linear`.
- Required field: `tracker.project_slug`.
- Default endpoint: `https://api.linear.app/graphql`.
- Canonical auth variable: `LINEAR_API_KEY`.
- Typical mapping in the provided workflow:
  - Active states: `Todo`, `In Progress`, `Merging`, `Rework`
  - Terminal states: `Closed`, `Cancelled`, `Canceled`, `Duplicate`, `Done`
- The workflow commonly relies on non-default team states such as `Human Review`, `Merging`, and
  `Rework`. Ensure these exist in your Linear team workflow when using this model.
- To find `tracker.project_slug`, open the project in Linear and copy the project URL; the slug is
  the URL segment used by Linear for that project.
- The optional `linear_graphql` extension tool is only meaningful when
  `tracker.kind == linear` and valid Linear auth is configured.
- Start from [`WORKFLOW.linear.md`](./WORKFLOW.linear.md) and align status names with your team
  workflow, including any custom review/merge states.

Linear front matter example:

```yaml
tracker:
  kind: linear
  endpoint: https://api.linear.app/graphql
  api_key: $LINEAR_API_KEY
  project_slug: "your-project-slug"
  active_states:
    - Todo
    - In Progress
    - Merging
    - Rework
  terminal_states:
    - Closed
    - Cancelled
    - Canceled
    - Duplicate
    - Done
```

## How to use it

1. Make sure your codebase is set up to work well with agents: see
   [Harness engineering](https://openai.com/index/harness-engineering/).
2. Pick the workflow template for your tracker and copy it to `WORKFLOW.md` in your runtime repo.
3. Set the required tracker credentials in environment variables.
4. Customize workspace hooks, state mapping, and prompt content in `WORKFLOW.md`.
5. Start the Symphony .NET service with your host project's standard `dotnet` run command.

## Environment

- .NET SDK: `10.0.x`
- Target framework: `net10.0`

Common commands:

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

## Configuration

The `WORKFLOW.md` file uses YAML front matter for configuration and a Markdown body for the Codex
prompt.

Minimal tracker-agnostic skeleton:

```md
---
tracker:
  kind: github
  api_key: $GITHUB_TOKEN
  repository: "owner/repo"
workspace:
  root: ~/code/workspaces
hooks:
  after_create: |
    git clone git@github.com:your-org/your-repo.git .
agent:
  max_concurrent_agents: 10
  max_turns: 20
codex:
  command: codex app-server
---

You are working on issue {{ issue.identifier }}.

Title: {{ issue.title }} Body: {{ issue.description }}
```

Notes:

- If a value is missing, defaults are used where defined by the implementation/spec.
- `tracker.active_states` and `tracker.terminal_states` should be explicitly set for your tracker
  workflow.
- If `WORKFLOW.md` is missing or has invalid YAML at startup, Symphony does not boot.
- If a later reload fails, Symphony keeps running with the last known good workflow and logs the
  reload error until the file is fixed.

## Web dashboard

When enabled via service configuration (for example `server.port`), Symphony exposes:

- Dashboard at `/`
- JSON APIs under `/api/v1/*`

## Project Layout

- `ARCHITECTURE.md`: implementation architecture and technology decisions
- `WORKFLOW.github.md`: GitHub workflow template
- `WORKFLOW.azure-devops.md`: Azure DevOps workflow template
- `WORKFLOW.linear.md`: Linear workflow template
- `AGENTS.md`: coding conventions and engineering guidance for this implementation

## License

This project is licensed under the [Apache License 2.0](../LICENSE).
