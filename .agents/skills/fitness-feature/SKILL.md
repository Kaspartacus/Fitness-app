---
name: fitness-feature
description: Implement a small FitnessApp product feature end to end across the existing .NET 10 hosted Blazor WebAssembly layers. Use for new vertical slices or changes that require coordinated UI, API, business rules, persistence, authorization, tests, and project progress updates. Do not use for review-only, design-only, or pull-request-only work.
---

# Fitness Feature

Deliver one reviewable vertical slice without prebuilding future architecture.

## Inputs

- The user's requested outcome, constraints, and acceptance criteria.
- `AGENTS.md`, `docs/project.md`, `docs/progress.md`, and `.codex/checkpoint.md` when present.
- Existing implementation, tests, migrations, styling, and current design evidence.

## Workflow

1. Inspect the working tree and relevant code before proposing abstractions. Preserve unrelated and in-progress work.
2. Translate the request into observable acceptance criteria. Ask only when a missing decision would materially change scope, security, or stored data; make routine implementation choices yourself.
3. Inspect the relevant current design source. Record whether the result is directly verified, a consistent extension, or unavailable evidence.
4. Trace the slice through the existing layers. Keep HTTP contracts in Contracts, orchestration contracts in Application, business rules in Domain, persistence in Infrastructure, composition and APIs in Server, and UI in Client.
5. Implement the smallest complete path, including loading, empty, validation, success, authorization, and recoverable failure states where applicable.
6. Derive identity and ownership on the server. Never accept a client-supplied user ID or role as authorization.
7. Add non-tautological tests for success, denial, invalid data, duplicate/race behavior, and history preservation as relevant.
8. Verify with `./scripts/verify.sh verify` when available. Run `./scripts/verify.sh audit` when dependencies change or before a release-quality handoff.
9. Exercise changed UI in the actual HTTPS app at mobile and desktop sizes when browser tooling is available.
10. Remove superseded code paths, unused imports or dependencies, stale tests, obsolete configuration, dead UI, generated output, and temporary tooling in the changed scope. Retain an item only when it has a concrete documented purpose.
11. Update durable product facts in `docs/project.md`, current evidence and next work in `docs/progress.md`, and the resumable state in `.codex/checkpoint.md`.

## Guardrails

- Keep application UI Danish and code plus technical documentation English.
- Preserve historical nutrition, activity, running, and weigh-in facts when definitions or goals change.
- Avoid speculative frameworks, generic repositories, empty modules, paid-service assumptions, and platform-specific dependencies that block Linux ARM64.
- Do not push, merge, deploy, or alter Figma unless the current user authorization explicitly permits it.

## Completion

Report changed behavior, files, exact verification results, design evidence level, remaining limitations, and the next safe action. A visually similar screen without working server behavior, persistence, authorization, and relevant tests is not complete.
