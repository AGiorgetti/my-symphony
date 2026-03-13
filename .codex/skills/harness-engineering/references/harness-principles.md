# Harness Engineering Principles

Source: OpenAI, "Harness engineering: leveraging Codex in an agent-first world" (2026-03-12), https://openai.com/index/harness-engineering/

## Why This Skill Exists

The article's central argument is that software discipline shifts from hand-writing code toward designing the scaffolding around agents: repository structure, validation loops, observability, architectural constraints, and cleanup systems.

## Core Principles

### 1. Make The Application Legible To Agents

- Human QA time becomes the bottleneck as agent throughput rises.
- Increase leverage by exposing UI, logs, metrics, traces, and other verification surfaces directly to the agent.
- Make the app bootable in isolated environments so the agent can reproduce and validate its own work.
- Turn fuzzy expectations into measurable checks.

### 2. Make The Repository The System Of Record

- Anything the agent cannot see in-context effectively does not exist.
- Keep `AGENTS.md` short and use it as a table of contents.
- Put durable knowledge into versioned repo artifacts such as docs, architecture maps, generated schemas, and executable plans.
- Favor progressive disclosure: small entry point first, then explicit pointers to deeper material.
- Add mechanical checks for freshness, structure, and cross-link health.

### 3. Optimize For Agent Legibility, Not Human Convenience Alone

- Choose technologies and abstractions the agent can inspect, reason about, validate, and modify directly.
- "Boring" dependencies often outperform opaque magic because they are composable, stable, and easier to model.
- Sometimes reimplementing a narrow internal helper is cheaper than fighting a black-box library.

### 4. Enforce Invariants, Not Every Implementation Choice

- Agent speed depends on strict boundaries and predictable structure.
- Enforce architectural rules with custom linters and structural tests.
- Validate data shapes at the boundary.
- Use lints for schema naming, structured logging, file-size limits, and other repeated invariants.
- Put remediation guidance in lint output so failures themselves become agent-readable instructions.

### 5. Feed Human Taste Back Into The System

- Review comments, refactoring feedback, and user-facing bugs should not die as one-off conversation.
- Convert repeated feedback into docs, tests, lints, scripts, or generated references.
- When documentation is too weak to prevent repetition, promote the rule into code.

### 6. Rethink Merge Friction In High-Throughput Systems

- The article describes minimal blocking merge gates, short-lived PRs, and cheap corrections as throughput grows.
- This only makes sense when fast validation and recovery already exist.
- Do not copy this blindly into a slower or less observable repository.

### 7. Treat Entropy As Continuous Garbage Collection

- Agents copy patterns that already exist, including mediocre ones.
- Define golden principles that prevent drift.
- Run recurring cleanup passes that scan for deviations and open small refactoring PRs.
- Continuous cleanup compounds better than rare "AI slop" cleanup days.

## Anti-Patterns To Avoid

- Giant `AGENTS.md` files that try to encode everything.
- Critical knowledge that exists only in chat, tickets, or people's heads.
- Opaque dependencies the agent cannot inspect or safely modify.
- Manual review comments that never get encoded into reusable rules.
- Big-bang cleanup projects instead of continuous garbage collection.
- Expanding autonomy before run, verify, recover, and escalate paths exist.

## Practical Audit Questions

- What does the agent still need humans to explain repeatedly?
- Which repository truths are still stored outside version control?
- What validation surfaces are missing for the next class of tasks?
- Which architectural rules exist socially but not mechanically?
- Which repeated review comments should become docs, lints, or tests?
- What recurring drift would be cheaper to catch daily than quarterly?

## Recommended Outputs

- A short top-level agent map.
- An indexed docs layout with domain references.
- Execution plans for multi-step changes.
- Structural tests and custom lints for important invariants.
- Reproducible validation scripts and measurable acceptance checks.
- Recurring cleanup automation or an explicit cleanup cadence.
