  Stop current work now following dotnet/IMPLEMENTATIONPLAN.github.md.

Attribution policy:
1. Ensure commit author identity is the currently logged-in GitHub user (`gh api user --jq '.login,.id'`).
2. End each progress update with:
Co-authored-by: Agent <agent@github.com>

Before stopping, do all of the following:
1. Update the existing Agent Work Log comment in the active issue with:
Current Step, Completed, Next Action, Changed Files, Validation Evidence, Blockers.
2. Add attribution line at the end of the progress update:
Co-authored-by: Agent <agent@github.com>
3. Keep issue open.
4. Keep or set status:in-progress, or set status:blocked if blocked.
5. Push current branch and update/open PR if there are local changes.
6. Ensure PR description contains precise resume instructions.

Return a stop summary with:
1. Issue link.
2. Branch name.
3. PR link.
4. Exact Next Action for resume.
5. Current blocker status.