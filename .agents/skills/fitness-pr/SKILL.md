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
3. Run `./scripts/verify.sh verify`; run `./scripts/verify.sh audit` when dependencies changed or current advisory evidence is required.
4. Review the final diff for unrelated files, secrets, personal data, generated databases, build outputs, whitespace errors, and misleading documentation.
5. Commit only when authorized, with a focused message. Push only the intended branch and never force-push unless the user explicitly requests and understands it.
6. Create or update the PR from [`.github/PULL_REQUEST_TEMPLATE.md`](../../../.github/PULL_REQUEST_TEMPLATE.md). Complete its Summary and scope, Affected modules/layers, impact assessment (migrations, API, authentication, authorization, secrets, and design; write `N/A` where not relevant), exact Verification and CI results, Known limitations, and Review handoff sections. Name the intended base branch and a specific review focus.
7. After creating or updating an open PR, and after every later agent-initiated push to its head branch, post one GitHub PR comment beginning with `@codex review` and including the current Review handoff focus. This starts the separate GitHub/Codex review; it is not direct messaging between Codex threads. Do not post duplicate triggers for the same head SHA.
8. Read the latest check run and the current GitHub/Codex review comment rather than relying on older successful results. Report pending, failed, or unavailable review evidence accurately; do not fix, merge, or approve the PR as its implementation agent.
9. Merge or deploy only under explicit current authorization and only after stated requirements are satisfied.
10. After an authorized merge, inspect every worktree. Delete the merged branch's worktree and local/remote branch, then prune stale worktree registrations. Preserve unmerged or active worktrees and never delete work containing uncommitted changes.

## Guardrails

- This skill never grants permission to commit, push, merge, deploy, alter Figma, or discard work.
- Do not amend, reset, merge, or rebase merely to simplify the history.
- Keep temporary stacked-PR CI triggers narrow and remove them when the stacked base is no longer needed.

## Completion

Return branch/base, commit SHA, PR URL and draft state, exact local verification, latest remote checks, remaining manual actions, and whether the branch is ready for review. Separate verified facts from assumptions.
