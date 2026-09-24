---
name: fitness-cleanup-maintenance
description: Perform a narrow, evidence-backed cleanup of FitnessApp without changing behavior, architecture, data, security, public contracts, or APIs. Use for the dedicated cleanup-maintainer workflow, not feature work, broad refactoring, dependency upgrades, or ordinary formatting.
---

# Fitness Cleanup Maintenance

Use this skill only for a dedicated maintenance run. The exact user message `Sæt i gang` selects this skill through the root `AGENTS.md`; the phrase embedded in a longer request does not.

## Scope and evidence

Remove only confirmed dead code, unused imports or dependencies, obsolete files or configuration, duplicate helpers, stale documentation, and temporary artifacts. Small naming or structural changes are allowed only when behavior is demonstrably unchanged.

Before deleting or changing an item, trace its references through source, project files, configuration, scripts, tests, documentation, generated outputs, reflection or convention-based loading, and public/API contracts as applicable. Preserve an item when use, ownership, runtime loading, data impact, or behavioral equivalence cannot be proved.

Never change architecture, functionality, security controls, migrations, database data, approved design, public contracts, or API behavior. Do not introduce features, broad refactors, speculative cleanup, dependency upgrades, or unrelated formatting. Never expose secrets or inspect their values, delete user-owned files, merge, deploy, force-push, reset, or delete an active or unmerged worktree.

## Run setup

1. From the repository root, fetch `origin`, record `origin/main`, inspect `git status --short --branch`, remotes, branches, and worktrees. Stop if `origin/main` cannot be fetched or resolved.
2. Preserve every unrelated tracked, untracked, or staged change. If the current checkout is not a clean, dedicated cleanup worktree, do review work read-only and use a separate worktree only after evidence shows changes are needed. Never discard or relocate another user's work.
3. Derive the application's listener ports from tracked configuration such as `Properties/launchSettings.json`; do not hardcode them. Before any cleanup, inspect those exact ports only. For each listener, verify the PID's command, working directory, and owner. Stop it with a graceful termination only when all three clearly identify this FitnessApp checkout; otherwise leave it running and report it. Do not use `sudo`, `pkill`, broad process matching, or forced termination.

## Review selection and local state

The ignored `.codex/cleanup-state.json` is local-only. Its entire schema is limited to:

```json
{
  "lastReviewedBaseCommit": "<full origin/main SHA>",
  "reviewedAt": "<UTC ISO-8601 timestamp>",
  "reviewMode": "full|incremental"
}
```

Do not add source inventories, paths, diffs, secrets, personal data, or PR metadata to it. Do not create or update it until all required verification succeeds.

- **First run, missing state, unavailable stored commit, or architecture change:** inspect the complete tracked solution, excluding generated `bin`/`obj`, database files, secrets, and temporary files. Produce a concise map of projects, dependencies, entry points, persistence, API, UI, tests, scripts, and startup flow before looking for cleanup candidates.
- **Later run:** review `lastReviewedBaseCommit..origin/main`, the current worktree, and dependency paths affected by those changes. Do not rescan the full solution unless an architecture change, missing state, or unavailable stored commit requires it.

## Change and verification workflow

1. Collect candidates from the selected review range, then prove each candidate unused before changing it. Prefer existing repository cleanup scripts or `dotnet clean` for generated output; do not delete ignored output simply because it is present.
2. If no candidate survives the evidence check, make no branch, commit, PR, or state update. Report that no changes were necessary and identify anything left unchanged because safety was unproven.
3. Only when a change is needed, create a unique `chore/code-cleanup-<suffix>` branch and isolated worktree from the latest fetched `origin/main`. Make the smallest evidence-backed edits there. Keep the dedicated worktree free of unrelated local changes.
4. Keep the existing hosted modular-monolith startup approach. Confirm that documentation has the canonical complete local startup command, `dotnet run --project src/FitnessApp.Server --launch-profile https`, rather than adding another host or startup project.
5. Run a short solution build with build servers disabled. Run only tests directly relevant to behavior changed by the cleanup; no test is required for documentation-only changes. Do not start the application to verify cleanup, and do not leave application servers, file watchers, test hosts, or background build processes running.
6. Check the derived listener ports again after cleanup. If this run started no application process, report their observed status; do not stop anything without the same command, working-directory, and owner proof.
7. If changed files pass verification, inspect `git diff --check`, commit a focused cleanup commit, push the new branch without force, and open a normal PR targeting `main` using the repository PR template. Never create an empty commit or PR, and never merge or deploy. Then write the minimal state file with the successfully reviewed `origin/main` SHA and current UTC timestamp.

## Dry run

For a non-mutating dry run, complete discovery, state selection, candidate evidence gathering, listener inspection, branch-name planning, and verification-command selection. Do not fetch, create a worktree or branch, edit files, clean output, stop processes, run build/tests, write state, commit, push, or open a PR. State clearly that it was a dry run and list the operations intentionally skipped.

## Completion report

End every real or dry run with a concise report containing:

- full or incremental review and the reviewed commit range;
- confirmed cleanup changes, or that none were necessary;
- exact build and directly relevant test results (or why they were not run);
- derived port and process status;
- the exact local startup command;
- branch, commit, and PR URL, or that no changes were necessary; and
- items deliberately left unchanged because safety could not be proven.
