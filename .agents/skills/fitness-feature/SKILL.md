---
name: fitness-feature
description: Implement a small FitnessApp product feature end to end across the existing .NET 10 hosted Blazor WebAssembly layers. Use for new vertical slices or changes that require coordinated UI, API, business rules, persistence, authorization, and tests. Do not use for review-only, design-only, or pull-request-only work.
---

# Fitness Feature

Deliver one reviewable vertical slice without prebuilding future architecture.

## Inputs

- The user's requested outcome, constraints, and acceptance criteria.
- The root and applicable nested `AGENTS.md` files, plus the Fitness App vault overview and relevant note in the `personal` Obsidian vault. Record unavailable vault access; use a local checkpoint only when a task hand-off needs one.
- Existing implementation, tests, migrations, styling, and current design evidence.

## Workflow

Use root task sizing first. The primary agent implements small/standard work itself; use the following orchestration only when delegation is warranted.

1. Start in the inner `Desktop/Fitness app/Fitness-app` checkout, confirm its Git root and status, and inspect relevant code before proposing abstractions. Select a branch using `$fitness-pr` **Branch lifecycle**; do not create a sibling worktree unless the owner explicitly requests isolation. Preserve unrelated and in-progress work.
2. Translate the request into observable acceptance criteria. Ask only when a missing decision would materially change scope, security, or stored data; make routine implementation choices yourself.
3. Inspect the relevant current design source. Record whether the result is directly verified, a consistent extension, or unavailable evidence.
4. Trace the slice through the existing `FitnessApp.slnx` projects. Keep HTTP contracts in Contracts, orchestration contracts in Application, business rules in Domain, persistence in Infrastructure, composition and APIs in the one Server host, and UI in Client. Do not add a feature-specific solution or host.
5. Implement the smallest complete path, including loading, empty, validation, success, authorization, and recoverable failure states where applicable.
6. Derive identity and ownership on the server. Never accept a client-supplied user ID or role as authorization.
7. Add non-tautological tests for success, denial, invalid data, duplicate/race behavior, and history preservation as relevant.
8. Assign verification to one owner. Select checks under root guidance and current user constraints; reuse results for unchanged inputs. Use `./scripts/verify.sh verify` for normal code changes and audit only when dependencies/advisory evidence require it, not simply because handoff is approaching.
9. Changed UI puts runtime/browser verification in scope by default: exercise affected interactions in the actual HTTPS app at relevant mobile and desktop viewport sizes. Skip only when the required tooling is unavailable or the user explicitly prohibits app/browser execution; report the exact limitation and remaining manual checks. Task size alone does not waive this check.
10. Remove superseded code paths, unused imports or dependencies, stale tests, obsolete configuration, dead UI, generated output, and temporary tooling in the changed scope. Retain an item only when it has a concrete documented purpose.
11. Complete the root `AGENTS.md` **Project knowledge loop** against the final diff, including any relevant stale guidance found while implementing. Include its knowledge status in the handoff; task-specific progress belongs only in ignored `.codex/checkpoint.md` when needed.

## Lean orchestration

- The primary agent is the coordinator, not another spawned planner. It owns sizing, acceptance criteria, a short execution plan, integration decisions, knowledge updates and the authorized Git/PR handoff. Discover enough to bound the task; do not repeat the implementer's detailed investigation.
- For large implementation tasks, delegate to `.codex/agents/implementer.toml`, then use the existing `reviewer` (or `security-reviewer` for a security-focused scope) in a separate read-only session. An independent GitHub review of the current diff can satisfy this stage if its evidence covers the required risks; do not also spawn a local reviewer for the same pass. Do not repurpose the implementation session as its reviewer. Read-only tasks need no implementer. If delegation is unavailable, disclose the limitation and use staged primary-agent work; never claim independent review occurred.
- Pass a compact brief: outcome, acceptance criteria, affected paths, relevant instructions, allowed edits, verification owner/commands and stopping conditions. Prefer fresh/minimal-history contexts where supported; no transcript, full-vault dump or repeated repository inventory. Include necessary safety constraints. Reviewer input is requirements, exact diff/base, direct dependencies and verification evidence, not a persuasive implementation narrative. Inspect code independently; expand context when evidence requires it.
- Only one agent owns code writes at a time. The coordinator does not edit those files concurrently. Freeze the reviewed diff; changes during review invalidate affected conclusions. Each handoff returns changed paths, results, unresolved risks and necessary next action, not raw logs. Do not launch agents merely to classify, summarize or wait.
- One initial review, then targeted follow-up for concrete findings or new risks. Retain coverage of the full unreviewed diff; do not repeat already-reviewed unchanged areas or rerun successful checks without changed inputs. Reuse the reviewer for follow-up, not the implementer. If findings repeat without progress, stop the review loop and report the blocker; no invented approval or arbitrary limit on necessary fixes.
- Review capacity is a ceiling, not a quota. Avoid overlapping local general/security reviews or redundant local/GitHub reviews of the same stable diff unless a distinct risk justifies them; retain required GitHub triggers and CI. Preserve user model/effort settings; the role name does not imply cheaper inference. No extra model calls to estimate tokens. When useful, note delegation/review counts and repeated work in the existing checkpoint; report token use only if measured, never infer it from account-wide quota percentages.

## Guardrails

- Keep application UI Danish and code plus technical documentation English.
- Preserve historical nutrition, activity, running, and weigh-in facts when definitions or goals change.
- Avoid speculative frameworks, generic repositories, empty modules, paid-service assumptions, and platform-specific dependencies that block Linux ARM64.
- Do not push, merge, deploy, or alter Figma unless the current user authorization explicitly permits it.

## Completion

Report changed behavior, files, exact verification results, design evidence level, remaining limitations, and the next safe action. A visually similar screen without working server behavior, persistence, authorization, and relevant tests is not complete.
