---
name: debug
description:
  Investigate stuck runs and execution failures by tracing service and agent logs
  with issue/session identifiers; use when runs stall, retry repeatedly, or
  fail unexpectedly.
---

# Debug

## Goals

- Find why a run is stuck, retrying, or failing.
- Correlate issue identity to an execution session quickly.
- Read the right logs in the right order to isolate root cause.

## Log Sources

- Primary runtime log: `log/symphony.log` (or configured equivalent)
  - In .NET deployments, log sink and file paths are typically configured in
    app settings or host logging setup.
  - Includes orchestrator, worker/agent runner, and session lifecycle events.
- Rotated runtime logs: `log/symphony.log*`
  - Check these when the relevant run is older.

## Correlation Keys

- `issue_identifier`: human ticket key (example: `MT-625`)
- `issue_id`: tracker UUID or stable internal issue ID
- `session_id`: execution session identifier (often `<thread_id>-<turn_id>`)

Treat these fields as join keys while debugging.

## Quick Triage (Stuck Run)

1. Confirm scheduler/worker symptoms for the issue.
2. Find recent lines for the issue (`issue_identifier` first).
3. Extract `session_id` from matching lines.
4. Trace that `session_id` across start, stream, completion/failure, and stall
   handling logs.
5. Classify the failure: timeout/stall, startup failure, turn failure, or
   orchestrator retry loop.

## Commands

```bash
# 1) Narrow by ticket key (fastest entry point)
rg -n "issue_identifier=MT-625" log/symphony.log*

# 2) If needed, narrow by issue UUID/internal ID
rg -n "issue_id=<issue-id>" log/symphony.log*

# 3) Pull session IDs seen for that issue
rg -o "session_id=[^ ;]+" log/symphony.log* | sort -u

# 4) Trace one session end-to-end
rg -n "session_id=<thread>-<turn>" log/symphony.log*

# 5) Focus on stuck/retry signals
rg -n "Issue stalled|scheduling retry|turn_timeout|turn_failed|session failed|session ended with error" log/symphony.log*
```

## Investigation Flow

1. Locate the issue slice:
   - Search by `issue_identifier=<KEY>`.
   - If noise is high, add `issue_id=<ID>`.
2. Establish timeline:
   - Identify first `session started ... session_id=...`.
   - Follow with `session completed`, `ended with error`, or worker exit lines.
3. Classify the problem:
   - Stall loop: `Issue stalled ... restarting with backoff`.
   - Startup failure: `session failed ...` before streaming/turn work.
   - Turn execution failure: `turn_failed`, `turn_cancelled`, `turn_timeout`, or
     `ended with error`.
   - Worker crash: `task exited ... reason=...`.
4. Validate scope:
   - Check whether failures are isolated to one issue/session or repeating across
     multiple issues.
5. Capture evidence:
   - Save key log lines with timestamps, `issue_identifier`, `issue_id`, and
     `session_id`.
   - Record probable root cause and exact failing stage.

## Session Lifecycle Reading

Read one session as a lifecycle:

1. `session started ... session_id=...`
2. Stream/lifecycle events for the same `session_id`
3. Terminal event:
   - `session completed ...`, or
   - `session ended with error ...`, or
   - `Issue stalled ... restarting with backoff`

For one-session investigations, keep the trace narrow:

1. Capture one `session_id` for the issue.
2. Build a timestamped slice for only that session:
   - `rg -n "session_id=<thread>-<turn>" log/symphony.log*`
3. Mark the failing stage:
   - Startup failure before stream events (`session failed ...`).
   - Turn/runtime failure after stream events (`turn_*` or `ended with error`).
   - Stall recovery (`Issue stalled ... restarting with backoff`).
4. Pair findings with `issue_identifier` and `issue_id` from nearby lines to
   avoid mixing concurrent retries.

## Completion Checklist

- Root cause is classified (startup, runtime, stall/retry, or crash).
- Evidence includes timestamped lines and correlation keys.
- Scope is known (single issue or systemic).
- Next action is clear (fix, config change, retry strategy, or escalation).

## Notes

- Prefer `rg` over `grep` for speed on large logs.
- Check rotated logs (`log/symphony.log*`) before concluding data is missing.
- If correlation fields are missing in new log statements, add them to logging
  conventions to improve future debugging.
