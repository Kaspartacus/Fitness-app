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
10. When the task's PR is confirmed merged into main, complete **Branch lifecycle** below. This owner-requested policy authorizes deletion of that completed branch locally and on origin after the safety checks; it does not authorize merging, deleting unrelated branches, removing worktrees, or discarding work.

## Branch lifecycle

For new work, fetch origin and branch from the latest `origin/main` in the existing checkout, preserving unrelated work:

- `feature/<short-kebab-case-description>`: new implementation or planned improvements, including tooling/documentation and behavior-preserving maintenance without a defect.
- `hotfix/<short-kebab-case-description>`: an important defect already in main requiring immediate correction; keep the fix narrowly scoped.
- `bugfix/<short-kebab-case-description>`: other unexpected behavior or defects without emergency priority.

Use a unique suffix when needed. Target main for all three; urgency never bypasses review or verification. Do not create new `fix/` or `chore/` branches. Preserve an existing active PR's branch name rather than renaming it mid-review; retire it after merge under the same rules.

At task completion, or the next authorized task that observes its completed PR, perform this cleanup without another routine confirmation:

1. Fetch/prune origin. Verify the exact PR is **merged**, not merely closed, into main. Resolve its head branch and recorded head SHA; never target main, a default/protected branch, another remote, or an unknown ref. Inspect all worktrees and local/remote refs. Confirm no open PR references the branch as head or base; unavailable PR information blocks deletion.
2. Require every existing target tip to equal the PR's recorded head and be an ancestor of current `origin/main`. A moved/reused branch, additional commits, squash/rebase merge without ancestry proof, or uncertain history is a blocker: preserve it and report the precise reason. Do not infer safety from the branch name or PR state alone.
3. Preserve staged, unstaged, untracked and ignored user data. If the branch is checked out, require a clean tracked/untracked worktree and prove local main can fast-forward to origin/main before using `git switch --no-overwrite-ignore main` and `git merge --ff-only --no-overwrite-ignore origin/main`. Block on any ignored-file collision in either operation. Never reset, stash automatically, or delete a checkout to make cleanup possible. A branch checked out in another worktree blocks deletion until that checkout is safely handled with separate authorization. Never remove the primary checkout or shared `.git`.
4. Report the exact local and remote targets. Recheck refs/PRs immediately before mutation. Use `git branch -d <exact-name>` locally, never `-D`. Guard remote deletion atomically with the verified PR-head SHA: `git push --force-with-lease=refs/heads/<exact-name>:<verified-sha> origin --delete <exact-name>`. Here the explicit lease is a compare-and-swap safety guard for deletion, not permission to rewrite history or delete unmerged work. Never fall back to unconditional deletion if the lease fails or is unavailable. Treat an already absent ref as complete; stop on changed evidence or command failure and report partial completion. Fetch/prune after deletion and verify both refs are absent.

Do not run a background watcher or merge to trigger cleanup. If a task ends before merge, report cleanup pending; reconcile at the next task with access. Bulk cleanup of unrelated branches still requires explicit exact-name confirmation under `$fitness-cleanup-maintenance`. Never delete unmerged work or an active PR.

## Guardrails

- This skill never grants permission to commit, push, merge, deploy, alter Figma, or discard work.
- Do not amend, reset, merge, or rebase merely to simplify the history.
- Keep temporary stacked-PR CI triggers narrow and remove them when the stacked base is no longer needed.

## Completion

Return branch/base, commit SHA, PR URL and draft state, exact local verification, latest remote checks, remaining manual actions, and whether the branch is ready for review. Separate verified facts from assumptions.
