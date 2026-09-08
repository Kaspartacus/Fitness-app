---
name: fitness-pr
description: Prepare, verify, create, or update a FitnessApp pull request from an existing branch. Use for branch hygiene, shared verification, sensitive-file checks, commit/push steps, PR metadata, and check monitoring. Do not use for feature implementation or standalone code review.
---

# Fitness PR

Prepare an honest review handoff without expanding the user's authorization.

## Inputs

- Current branch and intended base branch.
- User authorization for commit, push, PR creation/update, merge, or deployment.
- The branch diff, repository instructions, and current checkpoint.

## Workflow

1. Inspect branch, working tree, remotes, upstreams, commits, and the diff against the intended base.
2. Confirm the base contains the expected history and stop on unexpected divergence rather than overwriting it.
3. Run `./scripts/verify.sh verify`; run `./scripts/verify.sh audit` when dependencies changed or current advisory evidence is required.
4. Review the final diff for unrelated files, secrets, personal data, generated databases, build outputs, whitespace errors, and misleading documentation.
5. Commit only when authorized, with a focused message. Push only the intended branch and never force-push unless the user explicitly requests and understands it.
6. Create or update the PR with the correct base, scope, verification evidence, design evidence, limitations, and dependency relationship for stacked work.
7. Read the latest check run rather than relying on an older successful run. Report pending or failed checks accurately.
8. Merge or deploy only under explicit current authorization and only after stated requirements are satisfied.

## Guardrails

- This skill never grants permission to commit, push, merge, deploy, alter Figma, or discard work.
- Do not amend, reset, merge, or rebase merely to simplify the history.
- Keep temporary stacked-PR CI triggers narrow and remove them when the stacked base is no longer needed.

## Completion

Return branch/base, commit SHA, PR URL and draft state, exact local verification, latest remote checks, remaining manual actions, and whether the branch is ready for review. Separate verified facts from assumptions.
