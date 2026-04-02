# Symphony UI Architecture

Status: Current  
Applies to: `Symphony.Host` (Blazor Server, ASP.NET Core on .NET 10)

## Purpose

This document provides architectural guidelines for the production operator dashboard, as
specified in [`../SPEC_UI.md`](../SPEC_UI.md) and [`../SPEC.md`](../SPEC.md).

It complements [`ARCHITECTURE.md`](./ARCHITECTURE.md). All rules from the core architecture
document remain in force; this document adds UI-specific decisions on top of them.

Non-negotiable platform requirement: the dashboard implementation must remain fully
cross-platform across Windows, Linux, and macOS.

This document is guidance, not a strict guarantee of the runtime state. The implementation may
diverge temporarily as work progresses, but the intent is to keep the document aligned with the
current host UI whenever practical.

Alignment policy:

- Keep this document aligned with [`../SPEC.md`](../SPEC.md), [`../SPEC_UI.md`](../SPEC_UI.md),
  and the implemented behavior whenever practical.
- If implementation meaningfully changes operator-visible behavior, update this document in the
  same change when possible.
- If immediate sync is not feasible, treat documentation drift as technical debt and reconcile it
  in the next practical change.

For restart-safe migration context and checkpoint history, read
[`MUDBLAZOR_MIGRATION.md`](./MUDBLAZOR_MIGRATION.md) first.

## Component Library Reference

- **MudBlazor** — site: https://www.mudblazor.com/
- **Package** — `MudBlazor`
- **Docs policy**: use the official MudBlazor docs and source when verifying component APIs or
  behaviors. Do not assume signatures from older examples or prior library versions.

Version policy:

- Follow the version referenced by `Symphony.Host.csproj`.
- When updating MudBlazor, re-check provider placement, theme setup, and any JS-backed component
  assumptions before changing UI code.

---

## 1. Scope

This document covers:

- Technology choices and rationale for the dashboard UI
- Package integration and runtime asset configuration
- Application shell design (layout, navigation, routing)
- New data models introduced exclusively in the host layer
- Session activity store design (in-memory, host lifetime)
- Page catalog: dashboard, session list, session detail
- MudBlazor component assignment per UI element
- DI registration changes
- Auto-refresh and Blazor rendering strategy
- Cross-platform build notes for the current authored CSS approach
- Theme and layout guidelines for future UI work

This document does not cover:

- Core orchestration behavior
- Tracker adapter implementations
- API endpoint contracts
- MudBlazor component API details beyond the architectural mapping in this document

---

## 2. Technology Stack

| Layer | Choice | Reason |
|-------|--------|--------|
| Blazor hosting model | Blazor Server (`InteractiveServer`) | Already adopted in `Symphony.Host`; real-time updates via the existing server connection; no separate frontend deployment |
| UI component library | **MudBlazor** | Stable Blazor-native component set for tabs, tables, alerts, buttons, progress indicators, providers, and theming |
| Layout and page styling | **Authored CSS in `wwwroot/app.css`** | Predictable app-owned layout classes; no generated CSS build step; easier to reason about than utility churn |
| Theme system | `ThemeService` + `MudThemeCatalog` + HTML `data-theme` | Switchable palettes without rebuilding CSS; keeps existing theme keys while driving MudBlazor palettes |
| Lightweight UI primitives | Local Razor components | Used where simple HTML is clearer and more testable than a JS-backed third-party component |

### .NET / Package Compatibility

`Symphony.Host` targets `net10.0`. The current implementation uses MudBlazor `9.x`, which is the
intended compatibility line for the current host UI.

---

## 3. Package and Dependency Setup

### 3.1 NuGet Package

`Symphony.Host.csproj` references MudBlazor directly:

- `<PackageReference Include="MudBlazor" Version="9.2.0" />`

The project no longer references Flowbite.

### 3.2 Static UI Assets

`App.razor` loads:

- `_content/MudBlazor/MudBlazor.min.css`
- `app.css`
- `_content/MudBlazor/MudBlazor.min.js`

The host no longer serves `app.min.css` and no longer relies on a Tailwind CLI or generated CSS
asset pipeline.

### 3.3 Build Behavior

The UI build no longer requires a separate CSS preprocessing step. Cross-platform `dotnet build`
must succeed with only:

- the checked-in host stylesheet (`wwwroot/app.css`)
- MudBlazor package assets
- standard ASP.NET Core static-file behavior

---

## 4. CSS Configuration

### 4.1 `wwwroot/app.css`

`wwwroot/app.css` is the source of truth for host-specific layout and visual structure.

It contains:

- CSS custom property tokens for theme colors and surfaces
- `[data-theme="..."]` selector blocks for theme overrides
- shell layout classes
- page and panel spacing classes
- session table, timeline, and metadata layout classes
- shared primitives such as status pills, empty states, headers, and responsive grids

### 4.2 Token System

All brand and surface colors should flow through CSS custom properties, not ad hoc hard-coded
Razor classes.

Required token groups:

- `--color-primary-50` through `--color-primary-900`
- `--color-primary-DEFAULT`
- `--color-surface-base`
- `--color-surface-raised`
- `--color-surface-overlay`
- `--color-on-surface-primary`
- `--color-on-surface-secondary`

### 4.3 Theme Selectors

Built-in theme selectors are:

- `[data-theme="dark-yellow"]`
- `[data-theme="dark-blue"]`
- `[data-theme="light-blue"]`

### 4.4 Styling Rules

- Prefer semantic app classes over utility-style class churn.
- Keep layout, spacing, and responsive behavior in `app.css`.
- Use MudBlazor component parameters for component-level behavior; use authored CSS for host
  layout and visual language.
- Do not reintroduce a generated CSS build pipeline without an explicit architectural decision.

---

## 5. DI Registration Changes

### 5.1 `_Imports.razor`

`Components/_Imports.razor` should include:

- `@using MudBlazor`
- shared host component namespaces used throughout the UI

Flowbite namespace groups and static imports are no longer part of the host UI.

### 5.2 `SymphonyHostCompositionExtensions.cs`

In `AddSymphonyHost`:

- call `builder.Services.AddMudServices()`
- register `ISessionActivityStore` / `SessionActivityStore`
- register the page-facing dashboard/session data facade and fake-data mode resolver
- register the JSON export service plus the fake-data loader used for configured files and uploads
- register theme services used by the shell

`MudThemeProvider` and `MudSnackbarProvider` are hosted by `MainLayout.razor`.

---

## 6. Application Shell Design

### 6.1 Layout

The shell uses a responsive sidebar + top-bar pattern:

```
┌─────────────────────────────────────────────────────────┐
│  Top bar (mobile: menu button, title, live count)      │
├──────────────────┬──────────────────────────────────────┤
│  Left rail       │  Main Content (@Body)               │
│  (desktop:       │                                      │
│   persistent,    │                                      │
│   sticky,        │                                      │
│   own scroll)    │                                      │
│                  │                                      │
│  - Dashboard     │                                      │
│  - Sessions [N]  │                                      │
└──────────────────┴──────────────────────────────────────┘
│  Snackbar host                                          │
└─────────────────────────────────────────────────────────┘
```

### 6.2 `MainLayout.razor`

The shell is responsible for:

- `MudThemeProvider`
- `MudSnackbarProvider`
- responsive sidebar navigation
- live running-session count
- orchestrator start/stop controls
- theme switcher
- `ErrorBoundary` around `@Body`
- periodic polling of the page-facing dashboard/session data facade so shell indicators remain current for both live and fake page modes

### 6.3 Sidebar Rules

- On desktop, the left rail remains visible at all times.
- The left rail must be independently scrollable from the main content area when needed.
- On mobile, the rail becomes a temporary drawer with a scrim overlay.

---

## 7. Session Activity Store

### 7.1 Purpose

`SPEC_UI.md` requires that ended sessions remain visible for the current application run and that
each session exposes a browsable activity timeline.

The point-in-time dashboard snapshot is not sufficient on its own. A dedicated in-memory store
accumulates events per issue and preserves ended-session records for the process lifetime, without
touching lower architectural layers.

### 7.2 Key Design Constraints

- Lives entirely in the host layer
- Singleton lifetime, scoped to the host process
- Bounded per-session event log to prevent unbounded memory growth
- `DashboardStateService` is the single writer

### 7.3 New Models

| Type | Kind | Purpose |
|------|------|---------|
| `SessionActivityKind` | `enum` | Lifecycle milestone, agent message, warning, error, outcome, and related activity types |
| `SessionActivityEntry` | `record` | `Kind`, `Timestamp`, `Title`, and detail/summary data for the timeline |
| `SessionRecord` | `record` | `IssueIdentifier`, issue URL, start/end, final outcome/error, and active state |

### 7.4 Store Interface

Writer API:

- `RecordSessionStart(...)`
- `RecordActivity(...)`
- `RecordSessionEnd(...)`

Reader API:

- `GetAllSessions()`
- `GetActiveSessions()`
- `GetEndedSessions()`
- `GetSession(issueIdentifier)`
- `GetActivities(issueIdentifier)`

### 7.5 Implementation Guidelines

- Favor safe concurrent reads
- Keep writes non-disruptive to dashboard polling
- Preserve enough history for session inspection without allowing unbounded growth

---

## 8. Page Catalog

### 8.1 Routing

| Route | Page component | Purpose |
|-------|----------------|---------|
| `/` | `DashboardPage.razor` | System health + operational summary + active sessions overview |
| `/sessions` | `SessionListPage.razor` | All sessions from the current run with active/ended filtering |
| `/sessions/{Identifier}` | `SessionDetailPage.razor` | Full session detail with timeline and metadata |

All pages render inside `MainLayout`.

When `Dashboard:EnableFakeDataMode` is enabled, these routes may also be opened with `?mode=fake`
for host-only UI validation. Internal navigation preserves the `mode` query while moving between
dashboard, session list, and session detail pages. Fake mode is backed by a mutable in-memory
dataset store seeded from built-in fixtures, then optionally overlaid by a configured JSON file or
an interactive dashboard upload.

### 8.2 Dashboard Page (`/`)

Sections:

1. Operational summary strip
2. Active sessions panel
3. Retry queue panel
4. Recent attempts panel
5. Export / fake-data controls

Current component usage:

- `MudPaper` for cards and panels
- `MudAlert` for warning/failure states
- `MudSkeleton` on first paint
- `StatusPill` for compact statuses
- `InputFile` for fake-data JSON upload in fake mode

Auto-refresh: 5 seconds.

### 8.3 Session List Page (`/sessions`)

Sections:

1. Session explorer header inside the main page card
2. Filter bar via `MudTabs`
3. Session list table via `MudTable`
4. Empty state when no sessions match

Data sources:

- the page-facing dashboard/session data facade for both live and fake inventory/enrichment

Auto-refresh: 5 seconds.

### 8.4 Session Detail Page (`/sessions/{Identifier}`)

Sections:

1. Breadcrumb trail
2. Session header card
3. Timeline panel
4. Metadata panel

Responsive behavior:

- Desktop: split view with timeline as primary column and metadata as a secondary sticky column
- Narrower screens: tabbed Activity / Details fallback

Auto-refresh: 2 seconds for active sessions; no timer for ended sessions.

---

## 9. MudBlazor Component Assignments

High-level mapping:

| UI element | Current component |
|------------|-------------------|
| Summary / metric cards | `MudPaper` |
| Health / warning / failure messages | `MudAlert` |
| Primary and secondary actions | `MudButton` |
| Loading placeholders | `MudSkeleton` |
| Running indicator | `MudProgressCircular` |
| Session list filter | `MudTabs` |
| Session list table | `MudTable` |
| Providers | `MudThemeProvider`, `MudSnackbarProvider` |

Local primitives still intentionally used:

| UI element | Local primitive |
|------------|-----------------|
| Status chips | `StatusPill` |
| Theme menu | `ThemeSwitcher` custom menu |
| Breadcrumb trail | custom markup |
| Timeline | custom timeline layout |

Rule: prefer MudBlazor for structural/high-value components, but prefer simple local markup when
the third-party component adds unnecessary JS/runtime coupling or makes testing materially harder.

---

## 10. Auto-Refresh and Rendering Strategy

### 10.1 Pattern

All pages that auto-refresh use `PeriodicTimer` started from page lifecycle methods and implement
`IAsyncDisposable`. Avoid `System.Threading.Timer` for UI refresh loops.

Sequence:

1. Fetch initial data
2. Render loaded state
3. Start `PeriodicTimer` if the page requires refresh
4. On each tick: fetch data, update state, `InvokeAsync(StateHasChanged)`
5. Cancel timer in `DisposeAsync`

### 10.2 Refresh Intervals

| Page | Interval |
|------|---------|
| `DashboardPage` | 5 seconds |
| `SessionListPage` | 5 seconds |
| `SessionDetailPage` (active) | 2 seconds |
| `SessionDetailPage` (ended) | No timer |

### 10.3 ErrorBoundary Isolation

- Wrap the shell body in `ErrorBoundary`
- Wrap key dashboard panels in `ErrorBoundary`
- UI failures must never stop orchestration or the JSON API

---

## 11. Component Decomposition

Each page is broken into focused sub-components that receive typed model parameters and avoid
direct service injection unless they are true page/layout components.

```
Components/
  Dashboard/
    HealthSummaryCards.razor
    ActiveSessionsPanel.razor
    RetryQueuePanel.razor
    RecentAttemptsPanel.razor

  Sessions/
    SessionListTable.razor
    SessionStatusBadge.razor

  SessionDetail/
    SessionHeaderCard.razor
    SessionActivityTimeline.razor
    SessionMetadataPanel.razor

  Shared/
    StatusPill.razor

  Shell/
    ThemeSwitcher.razor
```

---

## 12. Data Flow Summary

```
          +--------------------------- live ---------------------------+
          |                                                            |
Orchestrator runtime + attempt history                                 |
        |                                                              |
        v                                                              |
DashboardStateService.GetSnapshotAsync()                               |
        |   +--- snapshot diff                                         |
        |               |                                              |
        |               v                                              |
        |       SessionActivityStore                                   |
        |                                                              |
        +---------------------------+----------------------------------+
                                    |
                                    v
                       Dashboard/session page data facade
                                    ^
                                    |
          +--------------------------- fake ---------------------------+
          |                                                            |
          |    FakeDashboardPageDataSource (in-memory canned dataset)   |
          +------------------------------------------------------------+
                                    |
                    +---------------+------------------+
                    v               v                  v
               DashboardPage   SessionListPage   SessionDetailPage
```

`DashboardStateService` remains the single write path into the live `SessionActivityStore`. Fake
mode uses its own host-only in-memory dataset and never mutates the live runtime or API state.

---

## 13. Navigation and Linking

- All navigation uses Blazor routing
- The sidebar Sessions item shows a live running-count badge
- Session detail pages are deep-linkable
- Operators can open multiple session-detail tabs in parallel browser tabs without losing context

---

## 14. Theming and Dark Mode

### 14.1 Defaults

The application launches with `dark-yellow` as the default theme:

- `<html class="dark" data-theme="dark-yellow">`
- `ThemeService` persists the selected theme
- `MudThemeCatalog` maps the current theme key into a `MudTheme`

### 14.2 Theme System

All colors are indirected through CSS custom properties in `app.css`.

A theme is a named set of token overrides applied via `[data-theme="..."]`.

This means:

- no runtime CSS regeneration
- no build-time theme switching step
- theme changes require only DOM attribute updates plus MudBlazor theme changes

### 14.3 Theme Service

`ThemeService` is responsible for:

- exposing `CurrentTheme`
- exposing `AvailableThemes`
- persisting the selection in `localStorage`
- toggling `<html>` `class="dark"` as needed
- setting `data-theme="{key}"`
- notifying the UI on theme changes

### 14.4 MudBlazor Theme Bridge

`MudThemeCatalog` keeps MudBlazor component palettes aligned with the current app theme so
MudBlazor controls and authored CSS continue to read as one system.

---

## 15. Cross-Platform Build

| Concern | Decision |
|---------|----------|
| CSS build tooling | None required beyond checked-in `app.css` |
| UI static assets | Served through standard ASP.NET Core static-file handling |
| Platforms | Windows, Linux, macOS |

The host UI should remain buildable with standard `dotnet build` across supported platforms.

---

## 16. Migration from Earlier UI Foundation

Completed migration outcomes that future work must preserve:

1. Flowbite package/usings/assets removed
2. Tailwind CLI/bootstrap/build-step removed
3. Generated `app.min.css` removed
4. MudBlazor providers and theme bridge added
5. Responsive layout moved into explicit app-owned CSS classes

Future work should extend the current MudBlazor + authored-CSS system rather than reintroducing
Flowbite or utility-first build dependencies by accident.

---

## 17. Security and Safety Rules

- Data rendered in the host UI comes from internal service snapshots and activity records
- Razor interpolation remains HTML-encoded by default
- Avoid rendering raw HTML unless content is explicitly trusted and sanitized
- Dashboard pages remain read-only in the current UI

---

## 18. Testing Guidance

### 18.1 Unit / component tests

- `SessionActivityStore`
- status-to-display mapping
- timeline rendering
- empty states and warning states

### 18.2 bUnit Guidance

When testing MudBlazor-backed components:

- prefer `JSInterop.Mode = JSRuntimeMode.Loose`
- stub `IKeyInterceptorService` when needed
- assert on user-visible behavior and `data-testid` hooks rather than vendor DOM internals

### 18.3 Validation Baseline

```bash
dotnet build dotnet/src/Symphony.Host/Symphony.Host.csproj -c Release
dotnet test dotnet/tests/Symphony.Host.IntegrationTests/Symphony.Host.IntegrationTests.csproj -c Release
dotnet test dotnet/Symphony.sln -c Release
```

---

## 19. Open Questions

- Should the operator shell expose additional orchestrator actions beyond start/stop?
- Should the session list gain secondary grouping or sorting controls?
- Should the session detail timeline eventually support pagination or section collapsing for very
  long histories?
- Should future themes remain built-in only, or become externally configurable?

---

## 20. Summary

| Concern | Decision |
|---------|---------|
| Component library | MudBlazor |
| CSS | Authored `wwwroot/app.css`, no Tailwind build |
| Layout | Responsive sidebar + top bar shell in `MainLayout.razor` |
| Pages | Dashboard `/`, Session List `/sessions`, Session Detail `/sessions/{id}` |
| Session history | In-memory `SessionActivityStore` in the host layer |
| Refresh | `PeriodicTimer` per page — 5s dashboard/list, 2s active detail |
| Theme system | `ThemeService` + `MudThemeCatalog` + `data-theme` |
| Cross-platform | No external CSS tooling required |
| Isolation | Dashboard failures must not affect orchestrator or JSON API |
