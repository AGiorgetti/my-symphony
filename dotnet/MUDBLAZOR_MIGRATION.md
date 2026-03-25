# MudBlazor Migration Workpad

Status: Complete
Owner: Codex
Scope: Replace `Flowbite Blazor` with `MudBlazor` in `Symphony.Host` while preserving the current UI feature set.

## Goals

- Preserve operator-visible behavior across dashboard, session list, and session detail pages.
- Keep the app buildable at each checkpoint when practical.
- Leave a restart-safe handoff trail so the migration can resume without rediscovery.

## Checkpoints

### Checkpoint 1: Migration foundation

- Add this workpad and handoff policy.
- Introduce MudBlazor package, DI registration, providers, and theme foundation.
- Keep Flowbite in place temporarily so component migration can proceed incrementally.

### Checkpoint 2: Shell and shared primitives

- Migrate layout shell, navigation, theme switcher, status chip, card, and empty-state primitives.
- Keep page content functionally unchanged where possible.

### Checkpoint 3: Page conversion

- Migrate dashboard components.
- Migrate session list components.
- Migrate session detail components.

### Checkpoint 4: Cleanup and validation

- Remove Flowbite package/usings/assets/scripts.
- Update docs and tests.
- Run build and test validation.

## Handoff Policy

If work stops mid-migration, the next agent should:

1. Read this file first.
2. Run `git status --short` and identify which checkpoint is currently in progress.
3. Inspect the latest completed validation notes in this file before changing code.
4. Continue from the first unchecked item instead of reworking already-migrated slices.
5. Update this file in the same change whenever a checkpoint advances or validation results change.

## Resume Rules

- Do not remove Flowbite until MudBlazor replacements for all current UI surfaces are in place.
- Prefer compatibility checkpoints over large one-shot refactors.
- Preserve existing routes, refresh loops, and `data-testid` hooks unless a test update requires a deliberate change.
- Tailwind has been removed; layout and responsive behavior now live in authored classes in `wwwroot/app.css`.

## Current Status

- [x] Migration workpad created.
- [x] MudBlazor package and providers added.
- [x] Shared shell primitives migrated.
- [x] Dashboard migrated.
- [x] Session list migrated.
- [x] Session detail migrated.
- [x] Flowbite removed.
- [x] Docs updated.
- [x] Validation complete.

## Validation Log

- `'/mnt/c/Program Files/dotnet/dotnet.exe' build dotnet/src/Symphony.Host/Symphony.Host.csproj -c Release`
- `'/mnt/c/Program Files/dotnet/dotnet.exe' test dotnet/tests/Symphony.Host.IntegrationTests/Symphony.Host.IntegrationTests.csproj -c Release`
- `'/mnt/c/Program Files/dotnet/dotnet.exe' build dotnet/Symphony.sln -c Release`
- `'/mnt/c/Program Files/dotnet/dotnet.exe' test dotnet/Symphony.sln -c Release`
