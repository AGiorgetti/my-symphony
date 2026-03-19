# Symphony .NET UI Dashboard — GitHub Implementation Plan (Agent-Executable)

Status: Implementation plan for the operator dashboard UI

## Purpose

This backlog is designed for autonomous or semi-autonomous coding agents working in GitHub.
It decomposes the UI dashboard implementation into dependency-ordered issues with clear
acceptance criteria, testing expectations, and definition of done.

Cross-platform is mandatory for all stories in this plan: implementations must work on
Windows, Linux, and macOS.

This plan follows:

- `../SPEC_UI.md`
- `../SPEC.md`
- `./AGENTS.md`
- `./ARCHITECTURE_UI.md`
- `./ARCHITECTURE.md`

## Relationship to IMPLEMENTATIONPLAN.github.md

This plan is the UI continuation of `IMPLEMENTATIONPLAN.github.md`. The foundation epics
(E1–E6) and the Blazor shell stories (E7-S1, E7-S2) from the original plan are
**prerequisites** for this plan. Stories here reference those issues using the notation
`upstream: E7-S1` to indicate cross-plan dependencies.

All issue lifecycle rules, completion tracking protocol, stop-and-resume protocol,
co-authorship policy, and label semantics are **identical** to `IMPLEMENTATIONPLAN.github.md`
and are not repeated here. Read that document first.

---

## Label Set

Reuse all labels defined in `IMPLEMENTATIONPLAN.github.md`. No new labels are required.
UI stories use: `area:dashboard`, `area:host`, `area:tests`, `type:epic`, `type:story`,
`priority:p0`, `priority:p1`, `priority:p2`, `status:ready`, `status:in-progress`,
`status:blocked`.

---

## Global Definition Of Done (UI stories)

Every UI story must satisfy all items from the base DoD in `IMPLEMENTATIONPLAN.github.md`
**plus** the following:

1. Before implementing any Flowbite Blazor component, load the AI context at
   `https://flowbite-blazor.org/llms-ctx.md` to obtain current component APIs.
   Do not assume API signatures from this document — always verify against the live context.
2. No hard-coded color values in Razor or C# — all colors flow through CSS custom property
   tokens (`--color-primary-*`, `--color-surface-*`, `--color-on-surface-*`).
3. New Razor components receive data via typed parameters only and do not inject services
   directly (service injection is the responsibility of page-level components).
4. All auto-refresh pages implement `IAsyncDisposable` and cancel their `PeriodicTimer` in
   `DisposeAsync`.
5. Dashboard failures (rendering, data fetch) must never propagate to the orchestrator
   background service or the JSON API. Every panel is wrapped in `ErrorBoundary`.
6. Docs updated when UI behavior changes: `ARCHITECTURE_UI.md`, `../SPEC_UI.md` as needed.
7. Build and runtime behavior introduced by UI stories must be cross-platform on Windows,
   Linux, and macOS.

---

## Phase Plan

- Phase UI-P0: Build Infrastructure + Shell + Session Store (foundation, must land first)
- Phase UI-P1: Theming + Pages (features, depends on P0)
- Phase UI-P2: Migration + Tests + Visual QA (hardening)

---

## Epic EU1: Build Infrastructure — Tailwind + Flowbite Integration

Labels: `type:epic`, `priority:p0`, `area:host`, `area:dashboard`

### Story EU1-S1: Integrate Flowbite Blazor NuGet and register DI services

Labels: `type:story`, `priority:p0`, `area:host`, `area:dashboard`
Blocked by: upstream E7-S1

Acceptance criteria:

1. Add `<PackageReference Include="Flowbite" />` to `Symphony.Host.csproj` without a pinned
   version. Always use the latest available release.
2. Call `builder.Services.AddFlowbite()` in `SymphonyHostCompositionExtensions.AddSymphonyHost`.
3. Add all Flowbite namespaces to `Components/_Imports.razor`:
   - `Flowbite.Base`, `Flowbite.Components`, `Flowbite.Components.Tabs`,
     `Flowbite.Components.Table`, `Flowbite.Icons`, `Flowbite.Services`, `Flowbite.Common`
   - Static `@using` for `Flowbite.Components.Button`, `Flowbite.Components.Tooltip`,
     `Flowbite.Components.Sidebar`, `Flowbite.Components.Dropdown`
4. `dotnet build -c Release` succeeds with Flowbite package resolved.

### Story EU1-S2: Set up Tailwind CSS v4 CLI and cross-platform MSBuild target

Labels: `type:story`, `priority:p0`, `area:host`
Blocked by: upstream E7-S1

Acceptance criteria:

1. Add `tools/` to `.gitignore` for `Symphony.Host`.
2. Document in `README.md` the one-time step to download the Tailwind CLI binary to
   `dotnet/src/Symphony.Host/tools/`:
   - Windows x64: `tailwindcss-windows-x64.exe` — rename to `tailwindcss.exe`
   - macOS ARM: `tailwindcss-macos-arm64` — rename to `tailwindcss`
   - Linux x64: `tailwindcss-linux-x64` — rename to `tailwindcss`
   - Releases: `https://github.com/tailwindlabs/tailwindcss/releases/latest`
3. Add a `Tailwind` MSBuild `Target` in `Symphony.Host.csproj`:
   - `BeforeTargets="Build"`
   - Windows condition (`'$(OS)' == 'Windows_NT'`): invoke `tools\tailwindcss.exe -i ./wwwroot/app.css -o ./wwwroot/app.min.css --minify`
   - Non-Windows condition: invoke `./tools/tailwindcss -i ./wwwroot/app.css -o ./wwwroot/app.min.css --minify`
   - Add `UpToDateCheckBuilt` items for `wwwroot/app.css` and `wwwroot/app.min.css`
4. `dotnet build -c Release` (with binary present) generates `wwwroot/app.min.css`.

### Story EU1-S3: Replace app.css with Tailwind v4 CSS-first config and theme token system

Labels: `type:story`, `priority:p0`, `area:host`, `area:dashboard`
Blocked by: EU1-S2

Acceptance criteria:

1. Replace `wwwroot/app.css` content with a Tailwind v4 CSS-first file:
   - `@import "tailwindcss"` at the top
   - `@source "../Components/**/*.razor"` and related `@source` directives to scan Razor files
   - `@plugin "flowbite/plugin"`
2. Define CSS custom property tokens in an `@theme` block following the naming convention:
   - `--color-primary-50` through `--color-primary-900` and `--color-primary-DEFAULT`
   - `--color-surface-base`, `--color-surface-raised`, `--color-surface-overlay`
   - `--color-on-surface-primary`, `--color-on-surface-secondary`
3. Add three `[data-theme="..."]` override blocks:
   - `dark-yellow`: dark surface tokens + yellow-400 (`#FBBF24`) primary family
   - `dark-blue`: dark surface tokens + blue-500 (`#3B82F6`) primary family
   - `light-blue`: light surface tokens + blue-500 primary family
4. Retain `.validation-message` and `#blazor-error-ui` styles.
5. Build produces `wwwroot/app.min.css` with all three theme blocks present.

### Story EU1-S4: Update App.razor for Flowbite scripts and default dark+yellow theme

Labels: `type:story`, `priority:p0`, `area:host`, `area:dashboard`
Blocked by: EU1-S1, EU1-S3

Acceptance criteria:

1. Replace `<link href="app.css" ...>` with `<link href="app.min.css" ...>`.
2. Add `<link rel="stylesheet" href="_content/Flowbite/flowbite.min.css" />`.
3. Before `</body>`, add Floating UI scripts (`@floating-ui/core` and `@floating-ui/dom`)
   followed by `<script src="_content/Flowbite/flowbite.js"></script>`.
4. Set `<html class="dark" data-theme="dark-yellow">` as the default.
5. Application loads in browser with dark background and yellow accent on first visit
   (no light flash).

### Story EU1-S5: Add CI step for Tailwind CLI binary provisioning

Labels: `type:story`, `priority:p0`, `area:host`
Blocked by: EU1-S2

Acceptance criteria:

1. In `.github/workflows/make-all.yml`, add a step before `dotnet build` that:
   - Detects `runner.os` (Windows / Linux / macOS)
   - Downloads the matching Tailwind CLI binary from the latest GitHub release
   - Renames it to `tailwindcss[.exe]` and places it at `dotnet/src/Symphony.Host/tools/`
   - Sets executable permission on non-Windows
2. CI build completes successfully with Tailwind CSS output generated.

---

## Epic EU2: Responsive Application Shell

Labels: `type:epic`, `priority:p0`, `area:host`, `area:dashboard`

### Story EU2-S1: Replace MainLayout with Flowbite-based responsive shell

Labels: `type:story`, `priority:p0`, `area:host`, `area:dashboard`
Blocked by: EU1-S1, EU1-S4

Acceptance criteria:

1. Replace `MainLayout.razor` with a Flowbite shell:
   - `Sidebar` with `CollapseMode="SidebarCollapseMode.Responsive"` (fixed, full height)
   - `SidebarLogo` with Symphony application name
   - Two `SidebarItem` entries: Dashboard (`/`) and Sessions (`/sessions`)
   - Sessions `SidebarItem` displays a live `Badge` showing the current running session count
   - Top `<nav>` bar with mobile hamburger `Button` toggle and app title
   - `<main>` content area properly offset from the fixed sidebar
2. `ErrorBoundary` wraps `@Body` — rendering failure in content area does not crash the shell.
3. `<ToastHost Position="ToastPosition.BottomRight" />` present in layout.
4. Layout `@code` polls `IDashboardStateService` every 5 seconds via `PeriodicTimer` to
   keep the Sessions badge count current. Implements `IAsyncDisposable`.
5. Sidebar collapses correctly at 375px viewport width (verified manually).
6. Content area uses responsive Tailwind grid — no fixed pixel widths.

---

## Epic EU3: Theming System

Labels: `type:epic`, `priority:p0`, `area:host`, `area:dashboard`

### Story EU3-S1: Implement IThemeService and ThemeService

Labels: `type:story`, `priority:p0`, `area:host`, `area:dashboard`
Blocked by: EU1-S3, EU1-S4, EU2-S1

Acceptance criteria:

1. Add `ThemeDescriptor` record: `Key` (string), `DisplayName`, `IsDark`.
2. Add `IThemeService` interface:
   - `string CurrentTheme { get; }`
   - `IReadOnlyList<ThemeDescriptor> AvailableThemes { get; }`
   - `event Action OnThemeChanged`
   - `Task SetThemeAsync(string key)`
3. Implement `ThemeService`:
   - `AvailableThemes` contains the three built-in themes: `dark-yellow`, `dark-blue`,
     `light-blue`.
   - `SetThemeAsync` calls JS interop to:
     1. Toggle `class="dark"` on `<html>` based on `ThemeDescriptor.IsDark`
     2. Set `data-theme="{key}"` on `<html>`
     3. Write `localStorage.setItem("symphony-theme", key)`
     4. Raise `OnThemeChanged`
   - On `OnInitializedAsync`, read `localStorage.getItem("symphony-theme")` via JS interop;
     if set and valid, call `SetThemeAsync` before first render to avoid a theme flash.
4. Register `IThemeService` / `ThemeService` as scoped (per Blazor circuit) in
   `SymphonyHostCompositionExtensions.AddSymphonyHost`.
5. Unit tests: `SetThemeAsync` with each built-in key sets correct `IsDark` flag;
   invalid key is ignored.

### Story EU3-S2: Implement ThemeSwitcher component

Labels: `type:story`, `priority:p1`, `area:dashboard`
Blocked by: EU3-S1

Acceptance criteria:

1. Add `ThemeSwitcher.razor` under `Components/Shell/`.
2. Renders a Flowbite `Dropdown` labeled with the current theme display name.
3. Dropdown lists all `IThemeService.AvailableThemes` entries.
4. Selecting a theme calls `IThemeService.SetThemeAsync`.
5. Component subscribes to `OnThemeChanged` and calls `StateHasChanged` to update the
   displayed current theme name.
6. Placed in the `MainLayout.razor` sidebar footer area.
7. Dropdown is usable and visible at 375px, 768px, and 1280px viewport widths.

---

## Epic EU4: Session Activity Store

Labels: `type:epic`, `priority:p0`, `area:host`, `area:dashboard`

### Story EU4-S1: Implement ISessionActivityStore and SessionActivityStore

Labels: `type:story`, `priority:p0`, `area:dashboard`
Blocked by: EU1-S1

Acceptance criteria:

1. Add models in `Symphony.Host.Dashboard` namespace:
   - `SessionActivityKind` enum: `LifecycleMilestone`, `AgentMessage`, `ProgressUpdate`,
     `Warning`, `Error`, `Outcome`
   - `SessionActivityEntry` record: `Kind`, `Timestamp`, `Title`, `Detail?`
   - `SessionRecord` record: `IssueIdentifier`, `IssueUrl?`, `StartedAt`, `EndedAt?`,
     `FinalOutcome?`, `FinalError?`, `IsActive`
2. Add `ISessionActivityStore` interface:
   - Writer: `RecordSessionStart(issueIdentifier, startedAt, issueUrl?)`
   - Writer: `RecordActivity(issueIdentifier, SessionActivityEntry)`
   - Writer: `RecordSessionEnd(issueIdentifier, endedAt, outcome, error?)`
   - Reader: `GetAllSessions()`, `GetActiveSessions()`, `GetEndedSessions()`
   - Reader: `GetSession(issueIdentifier)` returns `SessionRecord?`
   - Reader: `GetActivities(issueIdentifier)` returns `IReadOnlyList<SessionActivityEntry>`
3. Implement `SessionActivityStore`:
   - `ConcurrentDictionary<string, SessionState>` keyed by issue identifier
   - Each `SessionState` holds `SessionRecord` and `ImmutableList<SessionActivityEntry>`
   - Activity list bounded at 500 entries; when exceeded drop oldest and append synthetic
     `Warning` "Activity history trimmed" entry
   - All write methods swallow exceptions and log at Debug to never disrupt the orchestrator
4. Register as singleton in `SymphonyHostCompositionExtensions.AddSymphonyHost`.
5. Unit tests: session start/end cycle, activity recording, cap trimming, concurrent reads.

### Story EU4-S2: Implement snapshot diff and event detection in DashboardStateService

Labels: `type:story`, `priority:p0`, `area:dashboard`
Blocked by: EU4-S1

Acceptance criteria:

1. Add private `_lastSnapshot` field (nullable `DashboardSnapshot`) to
   `DashboardStateService`.
2. At the end of `GetSnapshotAsync`, under a `Lock`, diff the new snapshot against
   `_lastSnapshot` and call `ISessionActivityStore` methods for each detected change:
   - Issue identifier appeared in running list: `RecordSessionStart` +
     `LifecycleMilestone` "Session started"
   - `Status` changed: `LifecycleMilestone` with new status name
   - `TurnCount` increased: `ProgressUpdate` "Turn {n}"
   - `LastEvent` changed: `AgentMessage` with new content
   - Issue identifier disappeared from running list: `RecordSessionEnd` with outcome/error
   - New entry in retry list: `Warning` "Queued for retry" on the affected session
   - New entry in `RecentAttempts`: `Outcome` entry on the affected session
3. After diffing, set `_lastSnapshot` to the new snapshot.
4. Unit tests: each diff transition produces the expected `SessionActivityEntry` records.

---

## Epic EU5: Dashboard Page

Labels: `type:epic`, `priority:p1`, `area:dashboard`

### Story EU5-S1: Implement Dashboard page sub-components and page

Labels: `type:story`, `priority:p1`, `area:dashboard`
Blocked by: EU2-S1, EU4-S2

Acceptance criteria:

1. Add sub-components under `Components/Dashboard/`:
   - `HealthSummaryCards.razor`: four `Card` components for health, mode, last poll,
     workflow status. Typed parameters only; no service injection.
   - `ActiveSessionsPanel.razor`: active session list linking to `/sessions/{identifier}`,
     status `Badge` per row, `EmptyState` when empty.
   - `RetryQueuePanel.razor`: retry queue with next-retry ETA and failure reason,
     `EmptyState` when empty.
   - `RecentAttemptsPanel.razor`: last N attempts with outcome `Badge`, `EmptyState` when empty.
2. Each sub-component receives a typed parameter (relevant slice of `DashboardSnapshot`)
   and injects no services.
3. Update `DashboardPage.razor` at route `/`:
   - Metric cards grid: `grid-cols-1 sm:grid-cols-2 lg:grid-cols-4`
   - `Skeleton` on first paint before data loads
   - 5-second `PeriodicTimer` auto-refresh; implements `IAsyncDisposable`
   - Each panel wrapped in `ErrorBoundary`
4. `Alert` shown when health is degraded or snapshot contains errors.

---

## Epic EU6: Session List Page

Labels: `type:epic`, `priority:p1`, `area:dashboard`

### Story EU6-S1: Implement SessionStatusBadge shared component

Labels: `type:story`, `priority:p0`, `area:dashboard`
Blocked by: EU1-S1

Acceptance criteria:

1. Add `SessionStatusBadge.razor` under `Components/Sessions/`.
2. Accepts `RunAttemptStatus Status` parameter.
3. Maps status to `BadgeColor`:
   - `Succeeded`: green (Success)
   - Active statuses (`PreparingWorkspace`, `BuildingPrompt`, `LaunchingAgentProcess`,
     `InitializingSession`, `StreamingTurn`, `Finishing`): blue (Info)
   - `Failed`, `TimedOut`, `Stalled`: pink (Failure)
   - `CanceledByReconciliation`: gray
4. Renders a Flowbite `Badge` with the mapped color and status name as label.
5. bUnit test: each status value produces the correct `BadgeColor`.

### Story EU6-S2: Implement SessionListPage

Labels: `type:story`, `priority:p1`, `area:dashboard`
Blocked by: EU2-S1, EU4-S2, EU6-S1

Acceptance criteria:

1. Add `SessionListPage.razor` at route `/sessions`.
2. Filter bar: `Tabs Variant="TabVariant.Underline"` with tabs All, Active, Ended.
3. `Table Striped Hoverable` with columns: Issue (link), Status badge, Started,
   Ended/Duration, Outcome/last event. Each row links to `/sessions/{identifier}`.
4. `EmptyState` when filtered result is empty.
5. Data from `ISessionActivityStore` (lists) and `IDashboardStateService` (enrichment).
6. 5-second `PeriodicTimer` auto-refresh; implements `IAsyncDisposable`.
7. `SessionListTable.razor` sub-component under `Components/Sessions/` receives the
   filtered session list as a typed parameter; injects no services.

---

## Epic EU7: Session Detail Page

Labels: `type:epic`, `priority:p1`, `area:dashboard`

### Story EU7-S1: Implement SessionDetailPage header, activity timeline, and breadcrumb

Labels: `type:story`, `priority:p1`, `area:dashboard`
Blocked by: EU2-S1, EU4-S2, EU6-S1

Acceptance criteria:

1. Add `SessionDetailPage.razor` at route `/sessions/{Identifier}`.
2. Add `SessionHeaderCard.razor` under `Components/SessionDetail/`:
   - `Card`: identifier, status `SessionStatusBadge`, timestamps, tracker URL link.
   - Active sessions show a `Spinner` next to the status badge.
3. Add `SessionActivityTimeline.razor` under `Components/SessionDetail/`:
   - Flowbite `Timeline`; entries in chronological order.
   - `TimelinePoint` color by `SessionActivityKind`:
     `LifecycleMilestone` gray, `AgentMessage` blue, `ProgressUpdate` gray,
     `Warning` yellow, `Error` red, `Outcome` green/red.
   - Most-recent `Warning` or `Error` also shown as `Alert` above timeline.
4. `Breadcrumb`: Home > Sessions > {Identifier}.
5. `Alert Failure` shown when `FinalError` or any `Error` activity exists.
6. Sub-components accept typed parameters only.

### Story EU7-S2: Implement session metadata panel, detail tabs, and auto-refresh

Labels: `type:story`, `priority:p1`, `area:dashboard`
Blocked by: EU7-S1

Acceptance criteria:

1. Add `SessionMetadataPanel.razor` under `Components/SessionDetail/`:
   token totals (input/output/combined), turn count, session ID (thread+turn), attempt number.
2. `Tabs Variant="TabVariant.Underline"` on the page: Activity tab and Details tab.
3. Auto-refresh: 2-second `PeriodicTimer` for active sessions; no timer for ended sessions.
   Check `SessionRecord.IsActive` after first load; skip timer if false.
   Implements `IAsyncDisposable`.
4. Unknown `Identifier` renders gracefully (`EmptyState` or "Session not found").

---

## Epic EU8: Migration from Custom CSS Shell

Labels: `type:epic`, `priority:p2`, `area:dashboard`

### Story EU8-S1: Migrate and remove DashboardShellContent.razor

Labels: `type:story`, `priority:p2`, `area:dashboard`
Blocked by: EU5-S1

Acceptance criteria:

1. All content from `DashboardShellContent.razor` is now covered by new sub-components
   (EU5-S1) and the Flowbite `MainLayout` (EU2-S1).
2. `DashboardShellContent.razor` is deleted.
3. All bespoke CSS classes (`metric-card`, `pill`, `panel-list`, etc.) with no remaining
   usages are deleted from `app.css`.
4. `dotnet build -c Release` and `dotnet test -c Release` pass.

---

## Epic EU9: Tests and Visual QA

Labels: `type:epic`, `priority:p2`, `area:tests`, `area:dashboard`

### Story EU9-S1: bUnit sub-component unit tests

Labels: `type:story`, `priority:p1`, `area:tests`, `area:dashboard`
Blocked by: EU5-S1, EU6-S2, EU7-S2

Acceptance criteria:

1. Add `bunit` NuGet reference to `Symphony.Host.IntegrationTests` (or a new
   `Symphony.Host.ComponentTests` project if isolation is preferred).
2. bUnit tests for each sub-component with mock parameters:
   - `SessionStatusBadge`: each `RunAttemptStatus` value renders correct `BadgeColor`.
   - `HealthSummaryCards`: degraded health renders an `Alert`.
   - `SessionActivityTimeline`: entries render in order with correct point colors.
   - `ActiveSessionsPanel`, `SessionListTable`: `EmptyState` renders when list is empty.
3. All tests pass in CI.

### Story EU9-S2: Integration test startup validation for UI routes

Labels: `type:story`, `priority:p1`, `area:tests`, `area:dashboard`
Blocked by: EU6-S2, EU7-S2

Acceptance criteria:

1. Extend `Symphony.Host.IntegrationTests`:
   - `ISessionActivityStore` resolvable from DI.
   - `IThemeService` resolvable from DI.
   - `GET /` returns HTTP 200.
   - `GET /sessions` returns HTTP 200.
   - `GET /sessions/nonexistent-id` returns HTTP 200 (not a 500).
2. All assertions run in CI on PRs.

### Story EU9-S3: Visual QA checklist execution

Labels: `type:story`, `priority:p2`, `area:tests`, `area:dashboard`
Blocked by: EU8-S1, EU3-S2

Acceptance criteria:

All items verified manually and recorded in the story validation section:

1. Default `dark-yellow` theme renders on first load with no light flash.
2. Switching to `dark-blue` applies blue accent without reverting to yellow.
3. Switching to `light-blue` removes `class="dark"`, renders white background.
4. Page refresh restores last selected theme from `localStorage` without flash.
5. Sidebar collapses at 375px viewport width.
6. Theme switcher `Dropdown` usable at 375px, 768px, 1280px.
7. Dashboard metric cards: 1 col at 375px, 2 at 768px, 4 at 1280px.
8. `EmptyState` renders in each panel when no sessions exist.
9. Session detail `Timeline` shows all entries in chronological order.
10. Multiple `/sessions/{id}` tabs auto-refresh independently.
11. Simulated panel exception caught by `ErrorBoundary`; rest of page stays functional.

---

## Agent Execution Order

Steps on the same numbered line can run in parallel.

**Phase UI-P0: Foundation**

1. EU1-S1, EU1-S2 (parallel)
2. EU1-S3 (dep: EU1-S2), EU1-S5 (dep: EU1-S2) — parallel
3. EU1-S4 (dep: EU1-S1, EU1-S3)
4. EU2-S1 (dep: EU1-S1, EU1-S4), EU4-S1 (dep: EU1-S1) — parallel
5. EU4-S2 (dep: EU4-S1)

**Phase UI-P1: Theming + Pages**

6. EU3-S1 (dep: EU1-S3, EU1-S4, EU2-S1), EU6-S1 (dep: EU1-S1) — parallel
7. EU3-S2 (dep: EU3-S1)
8. EU5-S1 (dep: EU2-S1, EU4-S2), EU6-S2 (dep: EU2-S1, EU4-S2, EU6-S1),
   EU7-S1 (dep: EU2-S1, EU4-S2, EU6-S1) — parallel
9. EU7-S2 (dep: EU7-S1)

**Phase UI-P2: Migration + Tests + QA**

10. EU8-S1 (dep: EU5-S1)
11. EU9-S1 (dep: EU5-S1, EU6-S2, EU7-S2), EU9-S2 (dep: EU6-S2, EU7-S2) — parallel
12. EU9-S3 (dep: EU8-S1, EU3-S2)

---

## Suggested GitHub Milestone Mapping

- `Dashboard-P0`: EU1-S1, EU1-S2, EU1-S3, EU1-S4, EU1-S5, EU2-S1, EU4-S1, EU4-S2
- `Dashboard-P1`: EU3-S1, EU3-S2, EU5-S1, EU6-S1, EU6-S2, EU7-S1, EU7-S2, EU9-S1, EU9-S2
- `Dashboard-P2`: EU8-S1, EU9-S3

---

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

- upstream: #<issue from IMPLEMENTATIONPLAN.github.md if applicable>
- blocked-by: #<issue>

## Flowbite Component Reference

Before implementing Flowbite components, load the AI context:
https://flowbite-blazor.org/llms-ctx.md

## Agent Work Log

- Keep a single comment titled: ## Agent Work Log
- Update it with: Current Step, Completed, Next Action, Changed Files, Validation Evidence, Blockers
- End each update with: Co-authored-by: Agent <agent@github.com>

## Validation

- [ ] dotnet format --verify-no-changes
- [ ] dotnet build -c Release
- [ ] dotnet test -c Release
- [ ] No hard-coded color values in new Razor or C# code
- [ ] ErrorBoundary present on all new panels

## Docs

- [ ] Updated ARCHITECTURE_UI.md if UI architectural decisions changed
- [ ] Updated ../SPEC_UI.md if operator-visible behavior changed
```