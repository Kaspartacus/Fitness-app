# Development workflow

`AGENTS.md` files provide concise, durable routing and rules. [Project facts](project.md) explains shared architecture and product invariants; client design evidence lives with the client. Keep task status out of those documents.

## Focused changes and review

Work in focused, reviewable slices. Check the existing code and the applicable nested `AGENTS.md` before changing a module. Clean up replaced implementation, unused imports or dependencies, stale tests, obsolete configuration, dead UI, generated output, and temporary tooling in the changed scope.

Use the read-only `reviewer` agent for substantial changes. Also use `security-reviewer` when a diff touches authentication, authorization, ownership, secrets, logging, dependencies, configuration, or deployment. Both definitions are in `.codex/agents/`; reviews stay limited to the intended diff and supplement human approval.

## Project skills

Project skills are discovered from `.agents/skills/<skill-name>/SKILL.md`:

- `$fitness-feature` for an end-to-end vertical slice.
- `$fitness-design-check` for running UI and design-evidence checks.
- `$fitness-review` for a focused code review.
- `$fitness-pr` for branch and pull-request preparation.

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

If a task needs a hand-off note, use the ignored local `.codex/checkpoint.md` and record only the objective, Git state, completed work, verification, blockers, and the exact next action. Never add credentials, personal data, or conversation transcripts. Verify Git and the filesystem on resume instead of trusting a checkpoint alone.

## Pull requests

Before handoff, inspect the relevant diff and `git status`; run the proportionate checks. Run the shared verification before a normal code handoff and the audit when dependencies change or current advisory evidence is required. Commit, push, merge, deployment, and Figma changes require current user authorization. Never force-push, reset, or delete an active or unmerged worktree merely to simplify history.
