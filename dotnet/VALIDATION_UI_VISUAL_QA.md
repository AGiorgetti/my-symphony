# UI Visual QA

Manual browser verification for `EU9-S3` using Playwright against a local `Symphony.Host` run on March 20, 2026.

## Completed Checks

- Default first paint applied `class="dark"` with `data-theme="dark-yellow"` before interactive render completed.
- Switching to `dark-blue` updated the accent color and persisted `localStorage["symphony-theme"] = "dark-blue"`.
- Switching to `light-blue` removed the `dark` class, applied `data-theme="light-blue"`, and rendered a light surface palette.
- Refresh preserved the selected theme and the desktop sidebar trigger label now matches the restored theme after reload.
- Sidebar collapse and mobile overlay behavior were verified at `375px`.
- Theme switcher interaction was verified at `375px`, `768px`, and `1280px`.
- Dashboard summary cards rendered as `1` column at `375px`, `2` columns at `768px`, and `4` columns at `1280px`.
- Session detail timeline entries on `/sessions/%231` rendered in ascending timestamp order.

## Open QA Blockers

- The current local runtime always contains ended/retry data, so a fully empty dashboard state was not manually reproducible.
- The current local runtime had no active sessions, so independent multi-tab auto-refresh on `/sessions/{identifier}` could not be observed manually.
- A manual panel-failure scenario is not exposed in the live host, so the `ErrorBoundary` panel-isolation scenario remains covered by integration tests instead of browser repro.

## Observations

- The previous desktop regression where the theme switcher label fell back to `Dark Yellow` after reload was fixed by initializing the theme service inside the layout render circuit.
- Browser console still reports a Floating UI script error from the external CDN bundle and a missing `favicon.ico`. Those issues did not block the verified interactions above, but they remain follow-up candidates.
