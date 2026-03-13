Resume autonomous work using dotnet/IMPLEMENTATIONPLAN.github.md.

Attribution policy (mandatory):
1. Ensure commit author identity is the currently logged-in GitHub user (`gh api user --jq '.login,.id'`).
2. End each progress update and implementation commit with:
Co-authored-by: Agent <agent@github.com>

Resume protocol:
1. Find the active issue labeled status:in-progress, or the first unblocked status:ready priority:p0 issue if none is in progress.
2. Read issue body, dependencies, latest Agent Work Log, and linked PR.
3. Continue from the recorded Next Action.
4. Keep updating the same Agent Work Log comment (do not create new progress threads).
5. Include attribution at the end of each progress update:
Co-authored-by: Agent <agent@github.com>
6. Keep labels and dependency status accurate.
7. Run required validations and update PR/issue evidence.
8. If blocked, set status:blocked and document blocker details; otherwise continue to next eligible story.

At the end of this run, return:
1. Issue worked on.
2. Work completed this run.
3. Branch and PR link.
4. Updated Next Action.
5. Remaining blockers.