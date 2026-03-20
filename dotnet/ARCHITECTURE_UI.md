# Symphony UI Architecture

Status: Draft v1  
Applies to: `Symphony.Host` (Blazor Server, ASP.NET Core on .NET 10)

## Purpose

This document provides architectural guidelines for building the first production version of
the Symphony operator dashboard, as specified in [`../SPEC_UI.md`](../SPEC_UI.md) and
[`../SPEC.md`](../SPEC.md).

It complements [`ARCHITECTURE.md`](./ARCHITECTURE.md). All rules from the core architecture
document remain in force; this document adds UI-specific decisions on top of them.

Non-negotiable platform requirement: the dashboard implementation must remain fully
cross-platform across Windows, Linux, and macOS.

This document is guidance, not a strict guarantee of the current runtime state. The implementation may diverge temporarily as work progresses.

Alignment policy:

- Keep this document aligned with [../SPEC.md](../SPEC.md) and [../SPEC_UI.md](../SPEC_UI.md) and the implemented behavior whenever practical.
- If implementation meaningfully changes behavior, update this document and [../SPEC.md](../SPEC.md) and [../SPEC_UI.md](../SPEC_UI.md) in the same change when possible.
- If immediate sync is not feasible, treat documentation drift as technical debt and reconcile it in the next practical change.

## Component Library Reference

- **Flowbite Blazor** — site: https://flowbite-blazor.org/
- **AI / agent context** (always-current component docs): https://flowbite-blazor.org/llms-ctx.md

When implementing components, load the AI context URL to retrieve up-to-date API details,
parameters, and usage examples. Do not rely on cached or inlined snippets in this document.

**Version policy**: Always use the **latest release** of Flowbite Blazor. Do not pin to a
specific version in this document or in code reviews. Before implementing any component,
load the AI context URL to obtain current APIs — they may have changed since this document
was written.

---

## 1. Scope

This document covers:

- Technology choices and rationale for the dashboard UI
- Package integration and build configuration
- Application shell design (layout, navigation, routing)
- New data models introduced exclusively in the host layer
- Session activity store design (in-memory, host lifetime)
- Page catalog — dashboard, session list, session detail
- Flowbite Blazor component assignment per UI element
- DI registration changes
- Auto-refresh and Blazor rendering strategy
- Cross-platform build notes for Tailwind CSS
- Migration from existing custom CSS

This document does not cover:

- Core orchestration behavior (see [`ARCHITECTURE.md`](./ARCHITECTURE.md))
- Tracker adapter implementations
- API endpoint contracts (see `ARCHITECTURE.md` § API Contract Baseline)
- Flowbite Blazor component APIs — retrieve from the AI context URL above

---

## 2. Technology Stack

| Layer | Choice | Reason |
|-------|--------|--------|
| Blazor hosting model | Blazor Server (InteractiveServer) | Already adopted in `Symphony.Host`; real-time updates via existing SignalR connection; no separate deployment |
| UI component library | **Flowbite Blazor** (latest release) | Tailwind-native; typed API; accessible by default; multi-theme; no extra Node runtime |
| Theming | CSS custom property tokens (`@theme`) + `data-theme` attribute | Switchable palettes without rebuilding CSS; default: dark + yellow accent |
| CSS framework | **Tailwind CSS v4** | Required by Flowbite Blazor; CSS-first configuration via `@theme`; no `tailwind.config.js` complexity |
| Tailwind build | **Tailwind CSS CLI** (standalone binary) | No Node.js required; MSBuild-invokable; cross-platform binaries for Windows, Linux, macOS |
| Icon set | Flowbite Icons (included in `Flowbite` package) | Consistent visual language; typed `IconBase` parameters |

### .NET / Package Compatibility

Flowbite Blazor targets .NET 8 and .NET 9. `Symphony.Host` targets `net10.0`.
.NET 10 is ABI-compatible with earlier TFMs; the `Flowbite` package resolves its `net8.0`
or `net9.0` assets and works without modification.

---

## 3. Package and Dependency Setup

### 3.1 NuGet Package

Add `<PackageReference Include="Flowbite" />` to `Symphony.Host.csproj` **without pinning a
specific version**. Always target the latest available release (stable or prerelease).
Check https://www.nuget.org/packages/Flowbite for current releases. When a new version is
published, update the package and re-load the AI context URL to identify breaking API
changes before implementing or updating components.

### 3.2 Tailwind CSS v4 CLI

Download the platform-appropriate CLI binary to `dotnet/src/Symphony.Host/tools/` before
the first build. This is a one-time manual step (or CI provisioning step); the binary is
not committed to the repository. Add `tools/` to `.gitignore`.

Binaries are published at:
`https://github.com/tailwindlabs/tailwindcss/releases/latest`

Platform filenames:
- Windows x64: `tailwindcss-windows-x64.exe`
- macOS ARM: `tailwindcss-macos-arm64`
- Linux x64: `tailwindcss-linux-x64`

### 3.3 csproj Build Target

Add a single `Tailwind` MSBuild target that dispatches to the correct binary via `$(OS)`:

- Windows condition: `'$(OS)' == 'Windows_NT'` → calls `tools\tailwindcss.exe`
- Non-Windows condition: `'$(OS)' != 'Windows_NT'` → calls `tools/tailwindcss`
- Both variants: `-i ./wwwroot/app.css -o ./wwwroot/app.min.css --minify`
- Target runs `BeforeTargets="Build"`
- Add `UpToDateCheckBuilt` items for `app.css` and `app.min.css`

### 3.4 CI Step

Before `dotnet build` in CI, add a step that downloads the correct CLI binary based on
`runner.os` and places it at the path expected by the MSBuild target.

---

## 4. CSS Configuration

### 4.1 Replace wwwroot/app.css

Replace the existing hand-rolled CSS file with a Tailwind v4 CSS-first file:

- `@import "tailwindcss"` at the top
- `@source` directives pointing at `**/*.razor`, `**/*.cshtml`, `**/*.html` so the CLI
  scans Razor files for class usage
- `@plugin "flowbite/plugin"` for Flowbite component styles
- `@theme` block defining CSS custom property tokens for **all** color references. Tokens
  follow a naming convention (`--color-primary-*`, `--color-surface-*`,
  `--color-on-surface-*`). No component ever references a hard-coded Tailwind color class
  for brand colors — all colors flow through these tokens.
- `[data-theme="dark-yellow"]` selector block overriding token values for the default theme
  (dark background, yellow-400 primary). One additional block per built-in alternate theme.
  See §14 for the full theme system.
- Retain Blazor validation (`.validation-message`) and error boundary (`#blazor-error-ui`)
  styles using `@apply` or plain CSS

The CLI writes output to `wwwroot/app.min.css`.

### 4.2 Update App.razor

- Replace `<link href="app.css" ...>` with `app.min.css`
- Add `<link rel="stylesheet" href="_content/Flowbite/flowbite.min.css" />`
- Add `<script src="_content/Flowbite/flowbite.js"></script>` before `</body>`
- Add Floating UI scripts (`@floating-ui/core` and `@floating-ui/dom`) before `flowbite.js`
- Set `<html class="dark" data-theme="dark-yellow">` for the default dark + yellow theme.
  `class="dark"` activates Tailwind `dark:` variants on all Flowbite components.
  `data-theme="dark-yellow"` activates the yellow-accent CSS token overrides.
  The theme switcher (§14) updates both attributes via JS interop when the user changes
  themes.

For air-gapped environments, vendor the Floating UI scripts into `wwwroot/js/` instead of
loading from CDN.

---

## 5. DI Registration Changes

### 5.1 _Imports.razor

Add the following namespace groups to `Components/_Imports.razor`:

- `Flowbite.Base`, `Flowbite.Components`, `Flowbite.Components.Tabs`,
  `Flowbite.Components.Table`, `Flowbite.Icons`, `Flowbite.Services`, `Flowbite.Common`
- Static imports: `Flowbite.Components.Button`, `Flowbite.Components.Tooltip`,
  `Flowbite.Components.Sidebar`, `Flowbite.Components.Dropdown`

### 5.2 SymphonyHostCompositionExtensions.cs

In `AddSymphonyHost`, add:

- `builder.Services.AddFlowbite()` — registers Flowbite services (TwMerge, IFloatingService,
  IModalService, IToastService, etc.)
- `builder.Services.AddSingleton<ISessionActivityStore, SessionActivityStore>()` — new store
  (see §7)

The `<ToastHost />` component is placed in `MainLayout.razor`; no changes to
`MapSymphonyHost` are needed for toast support.

---

## 6. Application Shell Design

### 6.1 Layout

The shell uses a responsive sidebar + top bar pattern:

```
┌─────────────────────────────────────────────────────────┐
│  Top bar (mobile: hamburger toggle, title)               │
├──────────────────┬──────────────────────────────────────┤
│  Sidebar         │  Main Content (@Body)                 │
│  (responsive,    │                                       │
│   collapsible    │                                       │
│   on mobile)     │                                       │
│                  │                                       │
│  - Dashboard     │                                       │
│  - Sessions [N]  │                                       │
│    (badge count) │                                       │
└──────────────────┴──────────────────────────────────────┘
│  ToastHost (bottom-right)                                │
└─────────────────────────────────────────────────────────┘
```

### 6.2 MainLayout.razor

Replace the current minimal `LayoutComponentBase` with a Flowbite-based shell using:

- `Sidebar` with `CollapseMode="SidebarCollapseMode.Responsive"` (fixed, full height)
- `SidebarLogo`, `SidebarItemGroup`, `SidebarItem` for navigation entries
- Sidebar item for Sessions shows a live `Badge` with the running session count
- Top `<nav>` bar with a mobile hamburger `Button` and app title
- `<main>` content area offset from the fixed sidebar
- `ErrorBoundary` wrapping `@Body` — rendering failures here must not affect the
  orchestrator or JSON API
- `<ToastHost Position="ToastPosition.BottomRight" />` at the bottom of the shell
- Periodic poll of `IDashboardStateService` in `@code` to keep the sidebar badge current

---

## 7. Session Activity Store

### 7.1 Purpose

`SPEC_UI.md §9` requires that ended sessions remain visible for the current application run
and that each session exposes a browsable activity timeline.

The existing `DashboardStateService` provides a point-in-time snapshot only. A dedicated
in-memory store accumulates events per issue and preserves ended-session records for the
process lifetime, without touching lower architectural layers.

### 7.2 Key Design Constraints

- Lives entirely in `Symphony.Host.Dashboard` — no changes to Abstractions, Domain,
  Application, or Infrastructure.
- Singleton lifetime, scoped to the host process (lost on restart — acceptable per spec).
- Bounded per-session event log (default cap: 500 entries) to prevent unbounded memory growth.
- `DashboardStateService` is the **single writer** — it diffs consecutive snapshots and
  records events. No pub/sub bus is introduced.

### 7.3 New Models (Symphony.Host.Dashboard)

| Type | Kind | Purpose |
|------|------|---------|
| `SessionActivityKind` | `enum` | `LifecycleMilestone`, `AgentMessage`, `ProgressUpdate`, `Warning`, `Error`, `Outcome` |
| `SessionActivityEntry` | `record` | `Kind`, `Timestamp`, `Title`, `Detail?` |
| `SessionRecord` | `record` | `IssueIdentifier`, `IssueUrl?`, `StartedAt`, `EndedAt?`, `FinalOutcome?`, `FinalError?`, `IsActive` |

### 7.4 ISessionActivityStore Interface

Writer API (called by `DashboardStateService`):
- `RecordSessionStart(issueIdentifier, startedAt, issueUrl?)`
- `RecordActivity(issueIdentifier, SessionActivityEntry)`
- `RecordSessionEnd(issueIdentifier, endedAt, outcome, error?)`

Reader API (called by Blazor pages):
- `GetAllSessions()`, `GetActiveSessions()`, `GetEndedSessions()`
- `GetSession(issueIdentifier)` → `SessionRecord?`
- `GetActivities(issueIdentifier)` → `IReadOnlyList<SessionActivityEntry>`

### 7.5 SessionActivityStore Implementation Guidelines

- `ConcurrentDictionary<string, SessionState>` keyed by issue identifier for lock-free reads.
- Internal `SessionState` carries the current `SessionRecord` and an
  `ImmutableList<SessionActivityEntry>` (swapped on write, bounded by cap).
- If over cap: drop oldest entry, append a synthetic `Warning` "history trimmed" entry.
- Write methods must never throw — swallow and log as debug to avoid disrupting the
  orchestrator polling loop.

### 7.6 Event Detection in DashboardStateService

Add a private snapshot comparison routine called at the end of `GetSnapshotAsync`. It holds
`_lastSnapshot` as a private field (initially null) and diffs each new snapshot against it
under a `Lock`:

- Issue identifier **appeared** in running list → `RecordSessionStart` + `LifecycleMilestone`
  "Session started"
- `Status` changed → `LifecycleMilestone` with new status name
- `TurnCount` increased → `ProgressUpdate` with new count
- `LastEvent` or `LastMessage` changed → `AgentMessage` with new content
- Issue identifier **disappeared** from running list → look up outcome in `RecentAttempts`,
  call `RecordSessionEnd`
- New entry in retrying list → `Warning` entry on the affected session
- New entry in `RecentAttempts` → `Outcome` entry on the affected session

---

## 8. Page Catalog

### 8.1 Routing

| Route | Page component | Purpose |
|-------|----------------|---------|
| `/` | `DashboardPage.razor` | System health + operational summary + active sessions overview |
| `/sessions` | `SessionListPage.razor` | All sessions (active + ended) with status filter |
| `/sessions/{Identifier}` | `SessionDetailPage.razor` | Full session detail with activity timeline |

All pages inherit `MainLayout` via `Routes.razor`.

### 8.2 DashboardPage (`/`)

Quick-scan overview. Sections:

1. **Operational summary strip** — health, orchestrator mode, last poll tick, workflow config
   status. Sourced from `DashboardSnapshot`.
2. **Counters row** — running count, retry count, token totals, runtime seconds.
3. **Active sessions panel** — live list; each entry links to `/sessions/{identifier}`.
4. **Retry queue panel** — next retry ETAs and failure reasons.
5. **Recent attempts panel** — last N completed attempts with outcome badges.

Flowbite components: `Card` for summary cards; `Badge` for status/health; `Alert` for
errors and degraded health; `EmptyState` for empty panels; `Skeleton` on first paint before
data loads.

Auto-refresh: 5 seconds via `PeriodicTimer`.

### 8.3 SessionListPage (`/sessions`)

All sessions from the current run. Sections:

1. **Filter bar** — `Tabs` (All / Active / Ended).
2. **Session table** — `Table` with columns: Issue (link), Status badge, Started, Ended /
   Duration, Outcome / last event.
3. **Empty state** — `EmptyState` when no sessions match the filter.

Data sources: `ISessionActivityStore` for active/ended lists; `IDashboardStateService` for
current running-session details.

Auto-refresh: 5 seconds.

### 8.4 SessionDetailPage (`/sessions/{Identifier}`)

Full inspection view for one session. Layout:

```
┌─────────────────────────────────────────┐
│  Session: {Identifier}   [Status badge] │
│  Issue: {title/url}  Started: {time}    │
│  Ended: {time} / Running since {dur}    │
└─────────────────────────────────────────┘

┌────────────────┬────────────────────────┐
│  Activity tab  │  Details tab           │
│  (Timeline)    │  (metadata, tokens,    │
│                │   session ID, turns)   │
└────────────────┴────────────────────────┘
```

Sections:

1. **Session header** — `Card` with identifier, status `Badge`, timestamps. Active sessions
   show a `Spinner` next to the status. Link to tracker URL when available.
2. **Activity tab** — `Timeline` showing all `SessionActivityEntry` records in order.
   `SessionActivityKind` maps to timeline point color: `LifecycleMilestone` neutral,
   `AgentMessage` blue, `ProgressUpdate` gray, `Warning` yellow, `Error` red,
   `Outcome` green (Succeeded) or red (Failed/TimedOut). Most-recent warning is also shown
   as an `Alert` at the top of the panel.
3. **Details tab** — token totals (input/output/total), turn count, session ID
   (thread+turn), attempt number.
4. **Error panel** — `Alert Color="AlertColor.Failure"` when `FinalError` or any `Error`
   activity exists.

Implementation note: EU7-S1 delivers the breadcrumb, session header card, and activity
timeline first. EU7-S2 adds the metadata/details tab and starts a 2-second refresh loop only
for active sessions, while ended sessions render once without a timer.

Back navigation uses `Breadcrumb`: Home → Sessions → {Identifier}.

Auto-refresh: 2 seconds for active sessions; no timer for ended sessions (render once).

Multiple sessions: each detail page is a separate URL, so the operator can open several in
parallel browser tabs without losing context (satisfies SPEC_UI.md §6.5).

---

## 9. Flowbite Component Assignments

For component API details, parameters, and usage examples always load the AI context:
**https://flowbite-blazor.org/llms-ctx.md**

High-level component-to-purpose mapping:

| UI element | Flowbite component |
|------------|-------------------|
| Summary / metric cards | `Card` |
| Health / outcome status | `Badge` (color mapped to status) |
| Errors and warnings | `Alert` (Failure / Warning color) |
| Status → `BadgeColor` mapping | `Succeeded` → Success, active statuses → Info, `Failed`/`TimedOut`/`Stalled` → Pink/Failure, `Canceled` → Gray |
| Empty panel states | `EmptyState` |
| Loading skeleton on first paint | `Skeleton Variant="SkeletonVariant.Card"` |
| Sidebar navigation | `Sidebar`, `SidebarItem`, `SidebarLogo`, `SidebarItemGroup` |
| Mobile hamburger toggle | `Button` + Flowbite JS initializer |
| Session list filter | `Tabs Variant="TabVariant.Underline"` |
| Session table | `Table Striped Hoverable` |
| Session detail tabs (Activity / Details) | `Tabs Variant="TabVariant.Underline"` |
| Activity timeline | `Timeline`, `TimelineItem`, `TimelinePoint`, `TimelineContent` |
| Back navigation | `Breadcrumb`, `BreadcrumbItem` |
| Active session indicator | `Spinner` next to status badge |
| Toast notifications | `ToastHost` + `IToastService` |
| Icons | Flowbite Icons (`IconBase` typed parameters) |

---

## 10. Auto-Refresh and Rendering Strategy

### 10.1 Pattern

All pages that auto-refresh use `PeriodicTimer` started in `OnInitializedAsync` and
implement `IAsyncDisposable`. This avoids races associated with `System.Threading.Timer`.

Sequence on page load:
1. Fetch data once → assign to fields → set `_loaded = true`
2. Start `PeriodicTimer` with the page's configured interval
3. Background loop: wait for next tick → fetch → `InvokeAsync(StateHasChanged)`
4. `DisposeAsync` cancels the timer and cleans up

### 10.2 Refresh Intervals

| Page | Interval |
|------|---------|
| `DashboardPage` | 5 seconds |
| `SessionListPage` | 5 seconds |
| `SessionDetailPage` (active session) | 2 seconds |
| `SessionDetailPage` (ended session) | No timer; render once |

Check `SessionRecord.IsActive` after the first load; skip starting the timer if false.

### 10.3 ErrorBoundary Isolation

Wrap individual panels in `ErrorBoundary` so a single panel failure does not blank the whole
page. The outer `MainLayout.razor` `ErrorBoundary` catches full-page failures. Dashboard
failures must never stop orchestration or the JSON API.

---

## 11. Component Decomposition

Each page is broken into focused sub-components that receive typed model parameters and
inject no services, so they can be unit-tested with mock data.

```
Components/
  Dashboard/
    HealthSummaryCards.razor       <- health, mode, poll, workflow cards
    ActiveSessionsPanel.razor      <- active sessions list
    RetryQueuePanel.razor          <- retry queue
    RecentAttemptsPanel.razor      <- recent attempt outcomes

  Sessions/
    SessionListTable.razor         <- sessions table (active + ended)
    SessionStatusBadge.razor       <- reusable status -> BadgeColor mapping

  SessionDetail/
    SessionHeaderCard.razor        <- session summary card
    SessionActivityTimeline.razor  <- chronological activity list
    SessionMetadataPanel.razor     <- token counts, session ID, metadata
```

---

## 12. Data Flow Summary

```
Orchestrator (Background services)
        |
        v
IOrchestratorRuntimeService + AttemptHistoryTracker
        |
        v (called on each UI poll)
DashboardStateService.GetSnapshotAsync()
        |   +--- diff computation
        |               |
        |               v
        |       SessionActivityStore  (RecordActivity / RecordSessionStart / RecordSessionEnd)
        |               |
        |    +----------+------------------+
        v    v                             v
DashboardPage      SessionListPage     SessionDetailPage
(snapshot dto)   (ISessionActivityStore)  (ISessionActivityStore)
```

`DashboardStateService` is the only writer to `SessionActivityStore`, enforcing a single
write path and preventing concurrent diff races.

---

## 13. Navigation and Linking

- All navigation uses Blazor routing (`SidebarItem Href`, `NavLink`) — no JS push-state.
- The sidebar Sessions item shows a live `Badge` with the running count, refreshed by the
  layout's own periodic `IDashboardStateService` poll.
- Session detail back-navigation uses `Breadcrumb` (Home → Sessions → {Identifier}).
- Deep links (e.g. `/sessions/ABC-123`) resolve via the standard Blazor router.

---

## 14. Theming and Dark Mode

### 14.1 Defaults

The application launches with **dark mode and a yellow accent** — this is the default theme
(`dark-yellow`). No user action is required to activate it.

- `<html class="dark" data-theme="dark-yellow">` in `App.razor`
- Flowbite Blazor applies `dark:` Tailwind variants automatically when `dark` is present on
  `<html>`.
- Yellow-400 token overrides replace Flowbite's default blue primary palette.

### 14.2 Theme System

All colors are indirected through CSS custom properties defined in `@theme` in `app.css`.
A theme is a named set of token overrides applied via a `[data-theme="..."]` CSS selector
block. This means:

- Zero run-time JS color computation — themes are pure CSS.
- Any number of themes can coexist in the single compiled `app.min.css`.
- Switching themes requires only DOM attribute changes (`class`, `data-theme`), persisted
  in `localStorage`.
- Adding a new theme is a CSS-only change; no Razor or C# code changes required.

### 14.3 Built-in Themes

| Theme key | Mode | Accent |
|-----------|------|--------|
| `dark-yellow` | Dark | Yellow (`yellow-400` / `#FBBF24` family) — **default** |
| `dark-blue` | Dark | Blue (`blue-500` / `#3B82F6` family) |
| `light-blue` | Light | Blue (`blue-500` / `#3B82F6` family) |

### 14.4 Token Conventions

All components reference primary color through theme tokens, never through hard-coded
Tailwind color classes. Required tokens per theme block:

| Token group | Purpose |
|-------------|--------|
| `--color-primary-50` … `--color-primary-900` | Full primary scale for hover/focus states |
| `--color-primary-DEFAULT` | Base interactive color (buttons, links, active badges) |
| `--color-surface-base` | Page background |
| `--color-surface-raised` | Card / panel background |
| `--color-surface-overlay` | Modal / drawer background |
| `--color-on-surface-primary` | Primary text |
| `--color-on-surface-secondary` | Secondary / muted text |

### 14.5 ThemeService

Register a lightweight `IThemeService` / `ThemeService` scoped service in
`SymphonyHostCompositionExtensions.cs`:

- `CurrentTheme` — string property, default `"dark-yellow"`
- `AvailableThemes` — static list of `ThemeDescriptor` records (key, display name, is dark)
- `SetThemeAsync(string key)` — persists to `localStorage` and raises `OnThemeChanged`
  (C# event)
- On startup, `ThemeService` reads the persisted preference via JS interop
  (`localStorage.getItem("symphony-theme")`) and sets `CurrentTheme` before first render,
  so no theme flash occurs.

The `ThemeSwitcher.razor` sub-component lives in the `MainLayout.razor` sidebar footer and
renders a Flowbite `Dropdown` labeled with the current theme display name while listing all
available themes. `MainLayout.razor` injects the scoped theme service and passes it into the
sub-component as a typed parameter. The switcher subscribes to `OnThemeChanged` so the trigger
label updates in place after a selection. On selection it calls `IThemeService.SetThemeAsync`,
which invokes JS interop to:
1. Set or remove `class="dark"` on `<html>`
2. Set `data-theme="{key}"` on `<html>`
3. Persist `"symphony-theme"` in `localStorage`

### 14.6 Responsive Layout

`Sidebar CollapseMode="SidebarCollapseMode.Responsive"` handles mobile breakpoints
automatically. All content areas must use Tailwind responsive grid utilities — for example,
dashboard metric cards use `grid-cols-1 sm:grid-cols-2 lg:grid-cols-4`. No fixed pixel
widths are used for content panels. The theme switcher `Dropdown` is accessible at all
breakpoints.

---

## 15. Cross-Platform Build

| Platform | Binary name | MSBuild `$(OS)` condition |
|----------|-------------|--------------------------|
| Windows | `tailwindcss.exe` | `'$(OS)' == 'Windows_NT'` |
| Linux | `tailwindcss` | `'$(OS)' != 'Windows_NT'` |
| macOS | `tailwindcss` | `'$(OS)' != 'Windows_NT'` |

`$(OS)` reflects the build-agent OS reliably. `tools/` must be in `.gitignore`.

---

## 16. Migration from Existing Custom CSS

The current `DashboardShellContent.razor` uses bespoke classes (`metric-card`, `pill`,
`panel-list`, etc.) defined in `app.css`.

Incremental migration path:

1. Add `@import "tailwindcss"` at the top of `app.css` — existing custom classes coexist.
2. Replace one panel at a time in `DashboardShellContent.razor`: convert custom classes to
   Tailwind utilities and Flowbite components.
3. Delete each custom class from `app.css` as its last usage is migrated.
4. Once fully migrated, delete `DashboardShellContent.razor` and replace it with the new
   sub-components from §11.

---

## 17. Security and Safety Rules

- All data rendered in Blazor components originates from internal service snapshots; no
  user-supplied strings reach the DOM.
- Blazor's Razor engine HTML-encodes all `@value` interpolations by default. Never use
  `@((MarkupString)value)` unless the HTML is pre-sanitized internal content.
- Dashboard pages are read-only. No form submissions or state mutations are performed in
  this version.
- If agent output from `LastCodexMessage` is rendered directly in future, treat it as
  untrusted. Blazor's default encoding handles this if standard `@message` syntax is used.

---

## 18. Testing Guidance

### 18.1 Unit Tests

- `SessionActivityStore` — event recording, trimming at cap, session start/end, concurrent
  access.
- Diff detection in `DashboardStateService` — two-snapshot transitions produce correct
  `SessionActivityEntry` records.
- Sub-components — use `bUnit` to render with mock parameters and assert DOM output.

### 18.2 Integration Tests

- Extend `Symphony.Host.IntegrationTests` startup validation to assert that
  `ISessionActivityStore` is registered and that `/sessions` returns HTTP 200.

### 18.3 Visual QA

- Sidebar collapses correctly at 375px viewport width.
- Default dark + yellow theme renders on first load without a flash of light or wrong
  accent color (verify `<html class="dark" data-theme="dark-yellow">` before first paint).
- Theme switcher `Dropdown` is visible and usable at 375px, 768px, and 1280px widths.
- Switching to `dark-blue` applies blue accent on buttons, active nav items, and badges
  without reverting to yellow or default blue after a Blazor re-render.
- Switching to `light-blue` removes `class="dark"` and renders a white background with
  blue accent.
- Refreshing the page restores the last selected theme from `localStorage` without flash.
- `EmptyState` renders in each panel when no sessions exist.
- Session detail `Timeline` for a completed session shows all entries in order.
- Multiple browser tabs on different session detail URLs auto-refresh independently.
- Dashboard metric cards reflow from 1 column (375px) to 2 (768px) to 4 (1280px).

---

## 19. Open Questions

- Should the operator be able to trigger `POST /api/v1/refresh` from a dashboard button?
- Should ended sessions be grouped by outcome in the session list?
- Should the activity timeline paginate when entries exceed the cap?
- Should additional themes beyond the three built-in ones be user-creatable or loaded from
  an external file without recompiling the application?
- Should the theme preference sync across browser tabs within the same user session?
- Should Floating UI scripts be vendored locally rather than loaded from CDN?

---

## 20. Summary

| Concern | Decision |
|---------|---------|
| Component library | Flowbite Blazor — always latest release (docs: https://flowbite-blazor.org/llms-ctx.md) |
| Theming | Default dark + yellow accent; CSS token system; switchable via `data-theme`; persisted in `localStorage` |
| CSS | Tailwind CSS v4 via CLI, CSS-first `@import` + `@theme` |
| Layout | Sidebar + top bar shell in `MainLayout.razor` |
| Pages | Dashboard `/`, Session List `/sessions`, Session Detail `/sessions/{id}` |
| Session history | In-memory `SessionActivityStore` in `Symphony.Host.Dashboard`, written by `DashboardStateService` diff |
| Refresh | `PeriodicTimer` per page — 5s dashboard/list, 2s active detail |
| Data flow | One write path: `DashboardStateService` -> `ISessionActivityStore` |
| Compatibility | Flowbite targets .NET 8/9; resolved assets run on net10.0 without changes |
| Cross-platform | Tailwind CLI binary selected by MSBuild `$(OS)` condition |
| Isolation | Dashboard failures must not affect orchestrator or JSON API |
