---
name: harness-engineering
description: Design or improve an agent-first development harness for a repository. Use when Codex needs to make a codebase more legible, inspectable, and enforceable for coding agents by turning `AGENTS.md` into a short map, moving important knowledge into versioned repo docs, adding execution plans or quality scorecards, exposing UI or observability signals to local agents, encoding architectural invariants in lints or structural tests, or setting up recurring cleanup and doc-gardening loops.
---

# Harness Engineering

## Goal

Treat the repository as the agent's operating environment, not just a bag of source files. Optimize for agent legibility, progressive disclosure, mechanical enforcement, and continuous cleanup.

## Core Workflow

1. Audit the current harness.
2. Reshape repository knowledge into versioned, discoverable artifacts.
3. Increase application legibility and validation reach.
4. Encode invariants mechanically.
5. Convert recurring human feedback into repository-local rules.
6. Add lightweight cleanup loops that continuously reduce entropy.

## Audit The Current Harness

- Identify what the agent can directly inspect today: code, docs, generated artifacts, plans, local app runtime, logs, metrics, traces, CI output, and review feedback.
- List important knowledge that still lives outside the repository in chat, tickets, tribal knowledge, or human memory.
- Find where humans still act as brittle middleware: manual QA, repeated architecture explanations, flaky review gates, or repetitive style cleanups.
- Record the main bottleneck explicitly. Do not cargo-cult every harness pattern at once; target the smallest change that removes the next constraint.

## Reshape Repository Knowledge

- Keep `AGENTS.md` short and stable. Use it as a map, not an encyclopedia.
- Move durable knowledge into versioned markdown, generated artifacts, or scripts inside the repository.
- Prefer indexed `docs/` subtrees over one giant instruction file.
- Add cross-links, indexes, and "read next" pointers so the agent can traverse the knowledge base intentionally.
- Treat execution plans, completed plans, and known technical debt as first-class repository artifacts when work is large enough to justify them.
- When knowledge can drift, add a mechanical freshness check or a recurring cleanup task instead of trusting memory.

## Increase Application Legibility

- Make the app runnable in an isolated local environment whenever feasible.
- Prefer per-branch or per-worktree validation paths when the stack supports it.
- Expose the surfaces the agent needs to verify work: UI state, screenshots, DOM snapshots, logs, metrics, traces, and reproducible scripts.
- Translate vague goals into measurable checks such as startup latency, response-time budgets, or critical user journey assertions.
- If full observability is not yet realistic, start with the cheapest legibility improvements: reproducible run scripts, deterministic fixtures, better logs, and stable smoke tests.

## Encode Invariants, Not Micro-Style

- Enforce architecture with structural tests, dependency rules, schemas at boundaries, and custom lints.
- Centralize hard constraints such as layer boundaries, naming rules, logging requirements, file-size limits, or reliability requirements.
- Write lints and structural test failures as remediation instructions so the next agent can recover quickly.
- Allow local implementation freedom inside hard boundaries. The goal is correctness, maintainability, and future agent legibility, not uniform stylistic taste.
- If a review comment keeps recurring, promote it into documentation first, then into code or tooling if it still repeats.

## Design The Feedback Loop

- Treat review comments, bug reports, validation failures, and production incidents as signals about missing harness support.
- Feed those signals back into repository-local artifacts: docs, scripts, lints, tests, dashboards, or generated references.
- Prefer many cheap corrective loops over a single heavy manual gate only when the repository already has fast validation and recovery.
- Build recovery and escalation paths before increasing autonomy. Agents should know when to stop, retry, open a follow-up, or escalate to a human.

## Control Entropy

- Assume the agent will replicate existing patterns, including bad ones.
- Define a small set of golden principles that keep the codebase legible for future runs.
- Add recurring cleanup or doc-gardening work that scans for drift, updates quality scores, and opens small targeted fixes.
- Pay down technical debt continuously in small increments instead of scheduling rare cleanup sprints.

## Deliverables

- A short `AGENTS.md` that points to deeper sources of truth.
- A repository-local knowledge layout that is indexed and cross-linked.
- Execution plans or change logs for work that spans multiple steps or decisions.
- Mechanical enforcement for architectural and reliability invariants.
- A validation loop that lets the agent reproduce, verify, and recover from failures.
- A cleanup cadence or automation proposal for drift, stale docs, and repeated anti-patterns.

## Decision Rules

- Prefer repository-local, versioned truth over chat logs or external docs.
- Prefer boring, inspectable, composable dependencies over opaque magic.
- Prefer scaffolding that makes future tasks cheaper over one-off prompt hacks.
- Prefer encoding invariants at boundaries instead of prescribing every implementation detail.
- Prefer the smallest harness investment that unlocks the next meaningful step in autonomy.

## Reference

Read [references/harness-principles.md](references/harness-principles.md) when you need the rationale behind these patterns or want the article-derived heuristics and anti-patterns that informed this skill.
