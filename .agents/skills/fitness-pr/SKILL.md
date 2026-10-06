---
name: fitness-pr
description: Prepare, verify, create, or update a FitnessApp pull request from an existing branch. Use for branch hygiene, shared verification, sensitive-file checks, commit/push steps, PR metadata, and check monitoring. Do not use for feature implementation or standalone code review.
---

# Fitness PR

Prepare an honest review handoff without expanding the user's authorization.

## Inputs

- Current branch and intended base branch.
- User authorization for commit, push, PR creation/update, merge, or deployment.
- The branch diff, repository instructions, and an optional local task checkpoint.

## Workflow

1. Inspect branch, working tree, remotes, upstreams, commits, and the diff against the intended base.
2. Confirm the base contains the expected history and stop on unexpected divergence rather than overwriting it.
3. Select proportionate verification under root `AGENTS.md` and the current user's constraints. For normal code changes use `./scripts/verify.sh verify`; use `./scripts/verify.sh audit` when dependencies changed or current advisory evidence is required. Reuse still-valid results and state exactly what was skipped.
4. Review the final diff for unrelated files, secrets, personal data, generated databases, build outputs, whitespace errors, and misleading documentation.
5. Complete the root `AGENTS.md` **Project knowledge loop** for the intended diff; preserve existing pending handoffs until actually reconciled. Commit only when authorized, with a focused message. Push only the intended branch and never force-push.
6. Create or update the PR from [`.github/PULL_REQUEST_TEMPLATE.md`](../../../.github/PULL_REQUEST_TEMPLATE.md). Complete its scope, layers, impact assessment, **Project knowledge**, exact verification/CI results, limitations, and review handoff. Supply only the minimum non-sensitive, repository-supported context needed by a reviewer without vault access; never copy private notes wholesale. Name the base branch and specific review focus. Keep `@codex review` as the final line of every PR description.
7. After creating or updating an open PR, and after every later agent-initiated push to its head branch, post one GitHub PR comment beginning with `@codex review` and including the current Review handoff focus. This starts the separate GitHub/Codex review; it is not direct messaging between Codex threads. Do not post duplicate triggers for the same head SHA.
8. Read the latest checks and review for the current head rather than relying on older successes. Report pending, failed, or unavailable evidence accurately. Address confirmed findings when the implementation request authorizes fixes, then request review of the new head; never approve your own PR or treat AI review as owner approval.
9. Merge or deploy only under explicit current authorization and only after stated requirements are satisfied.
10. A merge does not authorize cleanup. Only with explicit cleanup authorization, inspect worktrees and their ignored local data before removing a verified clean linked checkout. Never remove the primary inner `Desktop/Fitness app/Fitness-app` checkout or its shared `.git`. Preserve unmerged branches and open PRs; branch deletion needs separate authorization and must not discard uncommitted work.

## Guardrails

- This skill never grants permission to commit, push, merge, deploy, alter Figma, or discard work.
- Do not amend, reset, merge, or rebase merely to simplify the history.
- Keep temporary stacked-PR CI triggers narrow and remove them when the stacked base is no longer needed.

## Completion

Return branch/base, commit SHA, PR URL and draft state, exact local verification, latest remote checks, remaining manual actions, and whether the branch is ready for review. Separate verified facts from assumptions.
