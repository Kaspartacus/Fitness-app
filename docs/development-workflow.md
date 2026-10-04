# Development workflow

`AGENTS.md` is the concise Codex router. `.ai/agent-os` describes task flow, `.ai/harness` basic rules, `.ai/context` recurring implementation context, `.ai/agents` role boundaries, and `.ai/skills` indexes Codex-native procedures. These Markdown files guide Codex; they do not implement an automatic orchestration runtime. For non-trivial work, actively read the Fitness App overview and relevant note in the private `personal` Obsidian vault. If it is unavailable, continue from repository evidence and report the gap. Keep task status out of tracked guidance.

## One checkout and one application

The primary local checkout is `Desktop/Fitness app/Fitness-app` (the inner directory). The outer `Fitness app` directory is only its container. Start or resume ordinary feature work there, confirm it with `git rev-parse --show-toplevel`, and use a Git branch in that checkout. Extend the existing `FitnessApp.slnx`, six `src/` projects, and single `FitnessApp.Server` host; do not create a sibling worktree, second solution, or second app process by default. An explicitly requested isolated checkout is an exception, not a new application.

Linked Git worktrees are separate copies of the same repository, but the default relative SQLite and Data Protection paths resolve under each checkout's Server content root. Running from another worktree can therefore look like an empty account or food catalogue. Before consolidating folders, inspect each worktree's tracked, untracked, and ignored data and active processes. Preserve open-PR branches and any unique local data; removing a clean linked worktree must not delete its branch or PR. Never remove the primary checkout or its shared `.git` directory. Codex desktop can create a worktree when a new project task uses its default environment, so select the saved project's local checkout when the intended workspace is the inner `Fitness-app` repository. Verify the saved Codex project path resolves to that inner directory.

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

Commit, push, merge, deployment, and Figma changes require current user authorization. Never force-push, reset, or discard an active worktree merely to simplify history. After a pull request has merged, inspect all worktrees and local data before cleanup. Remove a verified clean linked worktree only when its work is preserved; an owner-requested folder consolidation may remove a linked checkout for an unmerged branch while retaining that branch and its PR. Never remove the primary checkout. Do not delete a branch with uncommitted work or an open pull request, and do not delete a remote branch without explicit authorization.
