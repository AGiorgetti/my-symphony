# Symphony .NET UI Dashboard — GitHub Implementation Plan (Agent-Executable)

Status: Current implementation-aligned plan for the operator dashboard UI

## Purpose

This backlog is designed for autonomous or semi-autonomous coding agents working in GitHub.
It preserves the original dependency-ordered plan shape, but updates the work items to match the
current MudBlazor-based implementation and the authored CSS layout system.

Cross-platform is mandatory for all stories in this plan: implementations must work on
Windows, Linux, and macOS.

This plan follows:

- `../SPEC_UI.md`
- `../SPEC.md`
- `./AGENTS.md`
- `./ARCHITECTURE_UI.md`
- `./ARCHITECTURE.md`
- `./MUDBLAZOR_MIGRATION.md`

## Relationship to IMPLEMENTATIONPLAN.github.md

This plan is the UI continuation of `IMPLEMENTATIONPLAN.github.md`. The foundational host and
application stories from the base plan remain prerequisites for any UI work here.

All issue lifecycle rules, completion tracking protocol, stop-and-resume protocol, co-authorship
policy, and label semantics should remain aligned with `IMPLEMENTATIONPLAN.github.md`.

---

## Label Set

Reuse the existing labels from `IMPLEMENTATIONPLAN.github.md`. No UI-specific label expansion is
required.

---

## Global Definition Of Done (UI stories)

Every UI story must satisfy all items from the base DoD in `IMPLEMENTATIONPLAN.github.md`
plus the following:

1. No Flowbite or Tailwind build-pipeline reintroduction unless explicitly approved.
2. No hard-coded brand-color decisions in Razor or C#; route colors through app CSS tokens or
   MudBlazor theme colors.
3. New Razor sub-components receive data via typed parameters only and do not inject services
   directly unless they are true page or layout components.
4. All auto-refresh pages implement `IAsyncDisposable` and cancel their `PeriodicTimer` in
   `DisposeAsync`.
5. Dashboard failures must never propagate to the orchestrator background service or the JSON API.
6. Docs updated when UI behavior or architecture changes:
   - `ARCHITECTURE_UI.md`
   - `../SPEC_UI.md` as needed
7. Build and runtime behavior introduced by UI stories must remain cross-platform on Windows,
   Linux, and macOS.

---

## Phase Plan

- Phase UI-C0: Foundation complete
- Phase UI-C1: MudBlazor migration complete
- Phase UI-P1: Layout and density polish
- Phase UI-P2: Test and documentation hardening

---

## Epic EU1: MudBlazor Foundation and Host Wiring

Labels: `type:epic`, `priority:p0`, `area:host`, `area:dashboard`
Status: Completed

### Story EU1-S1: Integrate MudBlazor package and register DI services

Status: Completed

Delivered:

1. `MudBlazor` package referenced from `Symphony.Host.csproj`
2. `builder.Services.AddMudServices()` added in host composition
3. `@using MudBlazor` added to UI imports
4. Host builds successfully with MudBlazor assets resolved

### Story EU1-S2: Remove Tailwind build pipeline and generated stylesheet path

Status: Completed

Delivered:

1. Tailwind CLI/bootstrap MSBuild targets removed
2. `app.min.css` removed from the host asset path
3. Host now serves authored `wwwroot/app.css`
4. UI build no longer depends on platform-specific CSS tooling

### Story EU1-S3: Establish app-owned CSS token and layout system

Status: Completed

Delivered:

1. Theme token system preserved in `wwwroot/app.css`
2. Responsive layout classes moved into authored CSS
3. Page/panel/table/timeline shell classes consolidated into app-owned styles
4. Tailwind utility-class dependency removed from the host UI

### Story EU1-S4: Update App.razor and provider placement

Status: Completed

Delivered:

1. MudBlazor CSS and JS assets loaded
2. `app.css` loaded after MudBlazor CSS so host layout rules can intentionally override library defaults
3. Root theme attributes remain driven by the current theme system

---

## Epic EU2: Responsive Application Shell

Labels: `type:epic`, `priority:p0`, `area:host`, `area:dashboard`
Status: Completed

### Story EU2-S1: Replace legacy shell with MudBlazor-based responsive layout

Status: Completed

Delivered:

1. Responsive sidebar shell in `MainLayout.razor`
2. Top navigation bar with mobile menu button and live session indicator
3. Error boundary wrapping `@Body`
4. Snackbar provider hosted at layout level

### Story EU2-S2: Keep desktop sidebar persistent and independently scrollable

Status: Completed

Delivered:

1. Desktop left rail remains visible
2. Desktop left rail scrolls independently when needed
3. Mobile drawer behavior remains separate from desktop persistence

---

## Epic EU3: Theming System

Labels: `type:epic`, `priority:p0`, `area:host`, `area:dashboard`
Status: Completed

### Story EU3-S1: Implement `IThemeService` and runtime theme persistence

Status: Completed

Delivered:

1. Theme descriptors for built-in themes
2. Theme persistence via `localStorage`
3. HTML `dark` class and `data-theme` updates through JS interop
4. Theme change notifications for the current Blazor circuit

### Story EU3-S2: Bridge app theme keys to MudBlazor palettes

Status: Completed

Delivered:

1. `MudThemeCatalog`
2. Alignment between app tokens and MudBlazor component palette choices
3. Stable support for:
   - `dark-yellow`
   - `dark-blue`
   - `light-blue`

### Story EU3-S3: Implement shell theme switcher

Status: Completed

Delivered:

1. Theme switcher in the sidebar/footer area
2. Lightweight custom menu for predictable behavior and simpler tests
3. Theme change propagation without page reload

---

## Epic EU4: Session Activity Store

Labels: `type:epic`, `priority:p0`, `area:dashboard`
Status: Completed

### Story EU4-S1: Implement `ISessionActivityStore` and host-side session retention

Status: Completed

Delivered:

1. Session activity and record models
2. In-memory store preserving ended sessions for the current host lifetime
3. Session timeline access for detail views

### Story EU4-S2: Feed session activity from dashboard/runtime state transitions

Status: Completed

Delivered:

1. Session start/end visibility for the current run
2. Timeline event accumulation for detail pages
3. One-write-path model via dashboard/runtime state

---

## Epic EU5: Dashboard Page

Labels: `type:epic`, `priority:p1`, `area:dashboard`
Status: Completed

### Story EU5-S1: Implement dashboard summary and operational panels

Status: Completed

Delivered:

1. Health summary cards
2. Active sessions panel
3. Retry queue panel
4. Recent attempts panel
5. Warning, empty, and fallback states
6. 5-second auto-refresh loop

---

## Epic EU6: Session List Page

Labels: `type:epic`, `priority:p1`, `area:dashboard`
Status: Completed

### Story EU6-S1: Implement reusable session status primitive

Status: Completed

Delivered:

1. Shared session-status mapping
2. Local status-pill primitive aligned with the current host visual language

### Story EU6-S2: Implement `SessionListPage`

Status: Completed

Delivered:

1. Route `/sessions`
2. `MudTabs` filters for All / Active / Ended
3. `MudTable`-backed session list
4. Empty-state handling
5. 5-second auto-refresh loop

---

## Epic EU7: Session Detail Page

Labels: `type:epic`, `priority:p1`, `area:dashboard`
Status: Completed

### Story EU7-S1: Implement session header, breadcrumb, and timeline

Status: Completed

Delivered:

1. Session header card
2. Breadcrumb trail
3. Session timeline panel
4. Warning and failure attention states

### Story EU7-S2: Implement metadata panel, responsive detail layout, and active-session refresh

Status: Completed

Delivered:

1. Session metadata panel
2. Desktop split layout
3. Mobile tabs fallback
4. Active-session-only refresh loop
5. Graceful not-found handling

---

## Epic EU8: Migration Cleanup

Labels: `type:epic`, `priority:p2`, `area:dashboard`
Status: Completed

### Story EU8-S1: Remove obsolete Flowbite and Tailwind implementation paths

Status: Completed

Delivered:

1. Flowbite package/usings/assets removed
2. Tailwind CLI/bootstrap/build targets removed
3. Generated `app.min.css` removed
4. Host integration tests updated to current semantic markup and MudBlazor wiring

---

## Epic EU9: Tests and Visual QA

Labels: `type:epic`, `priority:p2`, `area:tests`, `area:dashboard`
Status: Active

### Story EU9-S1: Keep component tests aligned with semantic CSS classes

Acceptance criteria:

1. Tests assert current semantic classes and `data-testid` hooks.
2. Tests do not rely on removed Tailwind utility classes.
3. Tests do not rely on Flowbite component internals.

### Story EU9-S2: Maintain visual QA coverage for shell, dashboard, and session pages

Acceptance criteria:

1. Desktop shell verified at wide viewport.
2. Session detail split layout verified.
3. Session list filters and table verified.
4. Theme switching verified across built-in themes.

---

## Epic EU10: Layout Polish and UX Tightening

Labels: `type:epic`, `priority:p1`, `area:dashboard`
Status: Active

### Story EU10-S1: Continue spacing and density refinement

Acceptance criteria:

1. Remove redundant dead space in page and panel layouts.
2. Preserve scanability while reducing oversized vertical gaps.
3. Keep desktop and mobile spacing behavior intentional and consistent.

### Story EU10-S2: Refine session-detail split layout

Acceptance criteria:

1. Timeline and metadata columns feel visually balanced at desktop widths.
2. Sticky metadata behavior does not create awkward empty space.
3. Mobile/tab fallback remains intact.

### Story EU10-S3: Refine session list page information hierarchy

Acceptance criteria:

1. No redundant hero/banner panels above the session explorer card.
2. Page header information is integrated into the main content surface.
3. Refresh timestamp and filters remain easy to scan.

---

## Agent Execution Order

For future UI work:

1. Read `MUDBLAZOR_MIGRATION.md` and `ARCHITECTURE_UI.md`.
2. Prefer polish stories in EU9 and EU10 before introducing new UI surface area.
3. Keep tests aligned whenever semantic CSS classes or page structure changes.
4. Run host integration tests after any layout, shell, or page change.

---

## Suggested GitHub Milestone Mapping

- `Dashboard-Maintenance`: EU9-S1, EU9-S2
- `Dashboard-Polish`: EU10-S1, EU10-S2, EU10-S3

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

## UI Implementation Context

- Architecture: `dotnet/ARCHITECTURE_UI.md`
- Migration and handoff workpad: `dotnet/MUDBLAZOR_MIGRATION.md`

## Agent Work Log

- Keep a single comment titled: ## Agent Work Log
- Update it with: Current Step, Completed, Next Action, Changed Files, Validation Evidence, Blockers
- End each update with: Co-authored-by: Agent <agent@github.com>

## Validation

- [ ] dotnet build dotnet/src/Symphony.Host/Symphony.Host.csproj -c Release
- [ ] dotnet test dotnet/tests/Symphony.Host.IntegrationTests/Symphony.Host.IntegrationTests.csproj -c Release
- [ ] dotnet test dotnet/Symphony.sln -c Release
- [ ] No Flowbite or Tailwind-pipeline reintroduction
- [ ] Updated docs if architecture or operator-visible behavior changed

## Docs

- [ ] Updated ARCHITECTURE_UI.md if UI architectural decisions changed
- [ ] Updated ../SPEC_UI.md if operator-visible behavior changed
```
