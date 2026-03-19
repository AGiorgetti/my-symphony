# UI Specification Addendum

Status: Draft v1 (language agnostic, implementation agnostic)

Purpose: Define the operator-facing dashboard and session explorer experience for a Symphony implementation.

## 1. Relationship to the Core Specification

This document complements [SPEC.md](SPEC.md).

- [SPEC.md](SPEC.md) defines the core Symphony service behavior.
- This document defines the optional operator-facing UI behavior.
- This document focuses on user-visible features and operator needs.
- This version does not require persistence of session history across application restarts.

For .NET implementation guidelines see [`dotnet/ARCHITECTURE_UI.md`](dotnet/ARCHITECTURE_UI.md).

## 2. Purpose

The UI should help an operator understand what Symphony is doing during the current application run.

In this version, the UI is intended to:

- show which sessions are active right now
- keep ended sessions visible during the current application run
- let the operator inspect what happened inside a session
- help the operator understand progress, outcomes, and failures

The UI is an observability surface. It is not required for orchestrator correctness.

## 3. Primary User

The primary user is an operator or developer running Symphony who needs visibility into agent activity.

This user wants to answer questions like:

- What is running right now?
- What already ended in this application run?
- Which sessions failed?
- What did the agent report during a session?
- What happened inside a specific session?
- Can I inspect more than one session without losing context?

## 4. Scope for This Version

This version covers:

- active sessions
- ended sessions from the current application run
- session lists and dashboard views
- session detail pages
- browsing session messages, updates, and outcomes

This version does not require:

- persistence across application restarts
- replay of sessions from previous runs
- workflow editing from the UI
- issue tracker edits from the UI
- advanced comparison views
- long-term analytics or reporting

## 5. User Needs

### 5.1 Immediate visibility into current activity

The operator needs to see active sessions without reading logs or inspecting internal state manually.

### 5.2 Visibility after a session ends

The operator needs ended sessions to remain visible during the current application run so outcomes can be reviewed after completion.

### 5.3 Understanding of session behavior

The operator needs to inspect what a session did, including messages, updates, and major lifecycle events.

### 5.4 Fast diagnosis of failures

The operator needs enough information to understand whether a session succeeded, failed, stalled, timed out, or was cancelled.

### 5.5 Ability to inspect multiple sessions

The operator needs to open and inspect more than one session detail view during the same application run.

### 5.6 Readable and simple navigation

The operator needs the UI to be easy to read and easy to navigate, especially when multiple sessions are active.

## 6. Core UI Features

### 6.1 Dashboard

The dashboard is the main entry point for monitoring the current application run.

The dashboard must allow the operator to:

- see all active sessions
- see ended sessions from the current application run
- distinguish clearly between active and ended sessions
- identify the most recent activity for each session
- open a detail view for any visible session

The dashboard should make it easy to notice:

- sessions that are actively progressing
- sessions that have ended successfully
- sessions that have ended with failure
- sessions that may require attention

### 6.2 Session detail page

The session detail page is the main place where the operator inspects what happened inside a single session.

The session detail page must allow the operator to:

- see a summary of the session
- understand the session’s current or final status
- browse the session activity in order
- read messages and updates emitted by the agent
- inspect warnings, errors, and terminal outcomes

The session detail page should answer these questions clearly:

- What was this session working on?
- What happened first, next, and last?
- What did the agent report during the run?
- Did the session complete successfully?
- If not, what went wrong?

### 6.3 Session activity timeline

Each session should include a chronological activity view that helps the operator reconstruct the story of the run.

The activity timeline should include:

- lifecycle milestones
- agent messages
- progress or state updates
- warnings
- errors
- completion or failure outcome

The timeline should help the operator:

- understand the order of events
- find the last meaningful activity
- see where a session stopped or failed
- distinguish normal progress from abnormal behavior

### 6.4 Messages and updates

The UI must allow the operator to browse the messages and updates emitted during a session.

The operator should be able to:

- read session messages in context
- browse progress or state updates
- distinguish informational updates from warnings and errors

The messages and updates shown in the UI should be understandable to a human operator and useful for monitoring and debugging.

### 6.5 Multiple session inspection

The UI must support inspection of more than one session during the same application run.

For this version, it is sufficient that:

- the operator can open multiple session detail pages independently
- the operator can move between the dashboard and session details without losing context
- more than one session can be inspected during the same application run

This version does not require side-by-side comparison or split-view layouts.

## 7. Dashboard Expectations

The dashboard should present a clear overview of the current application run.

At a minimum, the dashboard should provide an operational summary section that shows:

- service health
- orchestrator mode
- last poll tick
- workflow configuration status
- running session count
- retry queue count
- token totals
- runtime totals

These summary items help the operator understand the overall state of the system before inspecting individual sessions.

At a minimum, each visible session entry should help the operator identify:

- the related issue or work item
- the session status
- when the session started
- when it ended, if applicable
- the latest meaningful activity
- whether the session ended with an error

The dashboard should prioritize clarity over density.

It should be possible to understand the state of the system quickly, without inspecting session details unless needed.

## 8. Session Detail Expectations

A session detail view should provide enough information for an operator to understand one session without reading raw logs.

The detail view should include:

- a clear session summary
- the related issue or work item information
- session status and outcome
- timestamps relevant to the session lifecycle
- a readable activity history
- visible messages and updates from the agent
- warnings and errors when present

The detail view should feel like an inspection page, not a raw protocol dump.

## 9. Session Lifetime Scope

In this version, session visibility is limited to the current application run.

This means:

- active sessions are visible while they are running
- ended sessions remain visible while the application remains running
- when the application stops or restarts, session history from that run may be lost

This behavior is acceptable for this version.

## 10. Out of Scope

The following are out of scope for this version:

- persistence of session history across application restarts
- recovery of session history from previous runs
- replay or resume of ended sessions
- editing workflow configuration from the UI
- changing issue tracker state from the UI
- advanced comparison workflows
- multi-user permissions and role-based UI behavior
- long-term reporting and analytics

## 11. User Stories

### 11.1 Dashboard

- As an operator, I want to see all currently active sessions so I can understand what the system is doing now.
- As an operator, I want to see ended sessions from the current application run so I can review recent outcomes.
- As an operator, I want to quickly distinguish running, successful, and failed sessions so I can focus on the sessions that need attention.

### 11.2 Session detail

- As an operator, I want to open a session detail page so I can understand what happened inside a specific session.
- As an operator, I want to browse the messages and updates emitted by the agent so I can follow its progress.
- As an operator, I want to see whether the session succeeded, failed, stalled, timed out, or was cancelled so I can understand the outcome.

### 11.3 Multi-session usage

- As an operator, I want to inspect more than one session during the same application run so I can monitor parallel work without losing context.

## 12. Acceptance Criteria

### 12.1 Dashboard

- The operator can see a list of active sessions.
- The operator can see a list of ended sessions from the current application run.
- The operator can distinguish active sessions from ended sessions.
- The operator can identify the status of each session at a glance.
- The operator can open a detail view for any visible session.
- The dashboard shows an operational summary including service health, orchestrator mode, last poll tick, workflow configuration status, running session count, retry queue count, token totals, and runtime totals.

### 12.2 Session detail

- The operator can view a chronological history of a session’s activity.
- The operator can browse messages and updates associated with a session.
- The operator can understand whether the session succeeded or failed.
- The operator can identify the last known useful activity for the session.
- The operator can see warnings and errors when they exist.

### 12.3 Session scope

- Ended sessions remain visible until the application stops.
- Session history from previous application runs is not required to be available.

## 13. Open Questions

The following questions are intentionally left open for later refinement:

- How much detail should be shown by default on the dashboard?
- How much raw detail should be exposed in the session detail view?
- Should session detail pages expose only human-readable summaries, or also lower-level diagnostic data?
- Should ended sessions be shown in one list, or grouped by outcome?
- Should the UI provide lightweight operator actions in a later version?

## 14. Summary

The UI for this version should give the operator clear visibility into active sessions and ended sessions within the current application run, and allow inspection of each session’s messages, updates, and outcome through a dedicated detail view.

The goal is clarity, not complexity.
