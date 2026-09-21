# Development workflow

`AGENTS.md` files provide concise, durable routing and rules. [Project facts](project.md) explains shared architecture and product invariants; client design evidence lives with the client. Keep task status out of those documents.

## Focused changes and review

Work in focused, reviewable slices. Check the existing code and the applicable nested `AGENTS.md` before changing a module. Clean up replaced implementation, unused imports or dependencies, stale tests, obsolete configuration, dead UI, generated output, and temporary tooling in the changed scope.

Use the read-only `reviewer` agent for substantial changes. Also use `security-reviewer` when a diff touches authentication, authorization, ownership, secrets, logging, dependencies, configuration, or deployment. Both definitions are in `.codex/agents/`, run with `sandbox_mode = "read-only"`, stay limited to the intended diff, and supplement human approval. They must not edit, commit, push, merge, deploy, approve, change settings, post GitHub feedback, or inspect secrets.

## Project skills

Project skills are discovered from `.agents/skills/<skill-name>/SKILL.md`:

- `$fitness-feature` for an end-to-end vertical slice.
- `$fitness-design-check` for running UI and design-evidence checks.
- `$fitness-review` for a focused code review.
- `$fitness-pr` for branch and pull-request preparation.
- `$fitness-pr-review` for the required read-only AI review and GitHub handoff for a feature PR.

Choose the skill for the primary outcome; a feature may use a design check or review as a later step. Keep skills in `.agents/skills`, the repository-supported discovery location, rather than in arbitrary nested module folders.

## Verification

The shared local and CI entrypoint is:

```bash
./scripts/verify.sh verify
./scripts/verify.sh audit
```

`verify` restores, builds, tests, and checks whitespace. It uses `VERIFY_DIFF_BASE` when set and otherwise needs a fetched `origin/main`. `audit` checks direct and transitive NuGet advisories for every project in `FitnessApp.slnx`; `self-test` checks the audit classifier without network access. The pull-request workflow uses these commands for pull requests to `main` with read-only repository permissions, no production secrets, and no deployment.

## Hook and temporary checkpoints

`.codex/hooks.json` defines an opt-in, non-mutating `SessionStart` context hook. Review and trust it through `/hooks`; do not alter Codex trust records. It can be invoked directly with `.codex/hooks/session-context.sh`.

If a task needs a hand-off note, use the ignored local `.codex/checkpoint.md` and record only the objective, Git state, completed work, verification, blockers, and the exact next action. For an open PR, also record its URL, base and head SHA, final AI review decision, and unresolved finding references. Never add credentials, personal data, review transcripts, or secrets. Verify Git and the filesystem on resume instead of trusting a checkpoint alone.

## Pull requests

Before handoff, inspect the relevant diff and `git status`; run proportionate checks. Run the shared verification before a normal code handoff and the audit when dependencies change or current advisory evidence is required. Use `.github/PULL_REQUEST_TEMPLATE.md` through `$fitness-pr`: every description must state summary and scope, affected modules/layers, relevant migration/API/authentication/authorization/secrets/design impact, exact local verification and CI results, known limitations, and review handoff with the base branch and a specific focus.

Every feature PR also receives one `$fitness-pr-review` pass. The coordinator reads the PR description, changed files, applicable `AGENTS.md` files, current CI status, and the diff against the PR base. It uses the read-only `reviewer` agent and adds the read-only `security-reviewer` when the security-sensitive scope applies. The final GitHub PR comment separates confirmed defects by severity with file references from suggestions and unanswered questions, then ends with exactly one decision: `CHANGES REQUIRED` plus a short English prompt to paste into the original feature Codex thread, or `READY FOR OWNER MERGE`.

GitHub PR comments are the durable handoff mechanism between separate Codex threads; one thread cannot message another automatically. The repository owner manually merges only after `READY FOR OWNER MERGE` and all required CI checks pass. To request a review, comment `@Codex review` on the PR. Automatic Codex GitHub reviews are enabled outside the repository through the Codex/GitHub integration: in Codex settings, enable **Code review** for the connected repository and turn on **Automatic reviews**. This repository does not add an LLM workflow, API key, auto-merge rule, or repository-setting automation. See the [official Codex GitHub review guide](https://learn.chatgpt.com/docs/third-party/github).

Commit, push, merge, deployment, and Figma changes require current user authorization. Never force-push, reset, or delete an active or unmerged worktree merely to simplify history. After a pull request has merged, inspect all worktrees before cleanup. Preserve any worktree with active or unmerged work. For the completed branch, remove its worktree, delete its local and remote branch, and run `git worktree prune` to clear stale registrations. Do not delete branches with uncommitted changes or an open pull request.
