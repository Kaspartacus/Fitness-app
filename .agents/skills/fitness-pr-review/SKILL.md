---
name: fitness-pr-review
description: Produce a consistent, read-only AI review and GitHub handoff for a FitnessApp feature pull request. Use after a PR exists; do not use to implement, push fixes, merge, deploy, prepare a PR, or change repository settings.
---

# Fitness PR Review

Review one feature PR without changing its branch, repository configuration, deployment, or secrets. The final GitHub PR comment is the cross-thread handoff; it is not an approval or merge action.

## Evidence

1. Read the PR description, base and head refs, changed files, CI status, and diff against the PR base. Record unavailable evidence rather than guessing.
2. Read the root `AGENTS.md` and only nested `AGENTS.md` files governing changed paths, then inspect the direct code and tests needed to understand changed behavior.
3. Use the read-only `reviewer`. Also use the read-only `security-reviewer` when the PR touches authentication, authorization, ownership, secrets, logging, dependencies, configuration, or deployment. No PR-explorer agent is needed: evidence collection is the coordinator's bounded task.

## GitHub handoff

Consolidate duplicates and post one GitHub PR comment with these sections, in order:

1. **Evidence considered** — description, base comparison, changed paths, applicable guidance, and CI state.
2. **Confirmed defects** — only reproducible or strongly evidenced defects, ordered `P0` through `P3`; each has `path:line`, impact, triggering condition, and reasoning. Write `None` when empty.
3. **Suggestions** — optional, non-blocking improvements. Write `None` when empty.
4. **Unanswered questions** — intent or evidence gaps that prevent a confident conclusion. Write `None` when empty.

Use GitHub PR comments as the handoff between separate Codex threads. They provide durable shared context; do not claim that one Codex thread can message another automatically.

## Decision

End the comment with exactly one final decision line and no other decision label:

- If any confirmed defect needs remediation or a required CI check is not passing, add an **Implementation handoff prompt** immediately before the final line. It must be short, English, copy-pasteable into the original feature Codex thread, cover every blocking finding, and ask it to update the same PR without merging and re-run proportionate validation. Final line: `CHANGES REQUIRED`.
- If no confirmed defects remain and required CI checks pass, final line: `READY FOR OWNER MERGE`.

Never merge, push fixes, deploy, change repository settings, invoke an LLM API, access secrets, credentials, ignored configuration, or GitHub Actions secrets. The repository owner manually merges only after `READY FOR OWNER MERGE` and the required CI checks pass. For an additional GitHub-hosted Codex review, comment `@Codex review`; automatic Codex GitHub reviews are configured outside the repository in the Codex/GitHub integration.
