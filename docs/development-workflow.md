# Development workflow

This repository keeps product implementation, review, verification, and pull-request preparation as separate responsibilities. `AGENTS.md` holds durable rules, `docs/project.md` holds product and architecture facts, `docs/progress.md` holds current implementation evidence and next work, and `.codex/checkpoint.md` is the concise resume record. Do not copy the same status narrative into every file.

## Feature cleanup

Cleanup belongs to the feature that makes an old path unnecessary. Before handoff, inspect the changed scope for replaced implementation, unused dependencies and imports, stale tests, obsolete configuration, unreachable UI, generated output, and temporary tooling. Remove what has no current purpose. Keep an item only when its near-term purpose is concrete and documented; do not retain speculative scaffolding.

## Project skills

Codex discovers project skills from `.agents/skills/<skill-name>/SKILL.md`:

- `$fitness-feature` implements a small end-to-end vertical slice.
- `$fitness-design-check` checks the running UI against current design evidence.
- `$fitness-review` performs a focused code-review pass.
- `$fitness-pr` prepares and verifies a branch or pull request.
- `$fitness-pr-review` performs the required read-only AI review and GitHub handoff for a feature PR.

Their descriptions intentionally do not overlap: choose by the primary requested outcome. A feature can invoke a design check or review as a later stage without turning those skills into implementation workflows.

## Read-only reviewer agents

The project defines `reviewer` and `security-reviewer` in `.codex/agents/`. Both inherit the active model and run with `sandbox_mode = "read-only"`.

- Use `reviewer` for substantial code changes before handoff.
- Also use `security-reviewer` when changes touch authentication, roles, authorization, ownership, secrets, logging, dependencies, configuration, or deployment.

Keep reviews bounded to the intended diff. Reviewer agents must not edit, commit, push, merge, deploy, approve, change settings, post GitHub feedback, or inspect secrets. AI review is supporting evidence, not human approval.

## Shared verification

Run the same entrypoint locally and in CI:

```bash
./scripts/verify.sh verify
./scripts/verify.sh audit
```

`verify` changes to the repository root, restores tools and packages, uses the proven serial build settings, runs the test suite, and checks tracked, staged, untracked, and committed-diff whitespace. It uses `VERIFY_DIFF_BASE` when set and otherwise requires the fetched `origin/main`; CI supplies the exact pull-request base SHA. `audit` refreshes restore assets and validates structured direct/transitive vulnerability output against every project identity in `FitnessApp.slnx`. Findings, missing projects, unexpected output, and unavailable advisory data all exit nonzero. `self-test` verifies those classifications without network access.

The pull-request workflow uses this script for pull requests targeting `main`, with read-only repository permissions, no production secrets, and no deployment.

## Resume hook and checkpoint

`.codex/hooks.json` registers one fast `SessionStart` hook for `startup|resume`. Its shell script performs no network calls or mutations and reports only the current branch, whether the worktree has changes, and the checkpoint path.

Project hooks do not run automatically until a user reviews and trusts their exact definition. In Codex CLI, open `/hooks`, inspect this repository's hook, and choose to trust it. Do not bypass hook trust or edit Codex trust records. The script can be tested safely before activation:

```bash
.codex/hooks/session-context.sh
```

At milestones, update `.codex/checkpoint.md` with the objective, branch and last commit, completed work, uncommitted work, verification, blockers, and exact next action. For an open PR, also record the URL, base and head SHA, final AI review decision, and unresolved finding references. Never store credentials, tokens, personal data, review transcripts, or secrets. On resume, compare the checkpoint with Git and the filesystem instead of trusting it blindly.

## Pull requests and authority

Work on a focused branch and keep each pull request reviewable. Use `.github/PULL_REQUEST_TEMPLATE.md` through `$fitness-pr`: every description must state summary and scope, affected modules/layers, relevant migration/API/authentication/authorization/secrets/design impact, exact local verification and CI results, known limitations, and review handoff with the base branch and a specific focus.

Every feature PR also receives one `$fitness-pr-review` pass. The coordinator reads the PR description, changed files, applicable `AGENTS.md` files, current CI status, and the diff against the PR base. It uses the read-only `reviewer` agent and adds the read-only `security-reviewer` when the security-sensitive scope applies. The final GitHub PR comment separates confirmed defects by severity with file references from suggestions and unanswered questions, then ends with exactly one decision: `CHANGES REQUIRED` plus a short English prompt to paste into the original feature Codex thread, or `READY FOR OWNER MERGE`.

GitHub PR comments are the durable handoff mechanism between separate Codex threads; one thread cannot message another automatically. The repository owner manually merges only after `READY FOR OWNER MERGE` and all required CI checks pass. To request a review, comment `@Codex review` on the PR. Automatic Codex GitHub reviews are enabled outside the repository through the Codex/GitHub integration: in Codex settings, enable **Code review** for the connected repository and turn on **Automatic reviews**. This repository does not add an LLM workflow, API key, auto-merge rule, or repository-setting automation. See the [official Codex GitHub review guide](https://learn.chatgpt.com/docs/third-party/github).

After a pull request has merged, inspect all worktrees before cleanup. Preserve any worktree with active or unmerged work. For the completed branch, remove its worktree, delete its local and remote branch, and run `git worktree prune` to clear stale registrations. Do not delete branches with uncommitted changes or an open pull request.
