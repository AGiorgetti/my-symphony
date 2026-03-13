Start autonomous execution for repository AGiorgetti/my-symphony using dotnet/IMPLEMENTATIONPLAN.github.md as the governing implementation plan.

Attribution policy (mandatory before other actions):
1. Resolve the logged-in GitHub user via `gh api user --jq '.login,.id'`.
2. Ensure commit author identity is the currently logged-in GitHub user (`gh api user --jq '.login,.id'`).
3. Use this fixed co-author trailer in every implementation commit and progress comment:
	- Co-authored-by: Agent <agent@github.com>

Do all setup and execution tasks required by the plan:
1. Create missing GitHub labels and milestones.
2. Create epic and story issues from the plan in dependency order.
3. Add blocked-by dependencies between issues.
4. Ensure each story issue includes acceptance criteria, validation checklist, and Agent Work Log requirements.
5. Enforce co-authorship policy in commits and issue progress comments.

Then start implementation immediately from the first unblocked priority:p0 story:
1. Move issue label to status:in-progress.
2. Create/update a single Agent Work Log comment with Current Step, Completed, Next Action, Changed Files, Validation Evidence, Blockers.
3. Implement code, run validations, open or update PR, and link PR to the issue.
4. Continue story by story until blocked or no ready stories remain.

At the end of this run, return:
1. What was created in GitHub (labels, milestones, issues, dependencies).
2. Which issue is currently in progress.
3. Branch and PR link.
4. Latest Agent Work Log summary.
5. Any blockers.