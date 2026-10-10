---
name: fitness-pr-review
description: Produce a consistent, read-only AI review and GitHub handoff for a FitnessApp feature pull request. Use after a PR exists; do not use to implement, push fixes, merge, deploy, prepare a PR, or change repository settings.
---

# Fitness PR Review

Review one feature PR without changing its branch, repository configuration, vault, deployment, or secrets. Return the review inline unless the user authorizes a GitHub comment. An authorized comment is a handoff, not an approval or merge action.

## Evidence

1. Read the PR description, base and head refs, changed files, CI status, and diff against the PR base. Record unavailable evidence rather than guessing.
2. Read the root `AGENTS.md` and only nested guidance governing changed paths. Follow the root **Project knowledge loop** in read-only mode: consult relevant vault notes when accessible, otherwise use the PR's **Project knowledge** handoff and repository evidence. Report missing intent or pending reconciliation, not invented access. Inspect direct code and tests needed to understand changed behavior.
3. Follow root `AGENTS.md` task sizing for the whole unreviewed diff. Small: the primary agent reviews directly, with no subagents. Standard: delegate one bounded review only when a concrete risk justifies it. Large: split useful independent review scopes within the root concurrency limit. Use `reviewer` or `security-reviewer` according to the actual risk; do not duplicate their scopes. Only the coordinator delegates, gathers evidence and consolidates the decision; delegated reviewers return findings without further delegation. No additional explorer agent is needed.

## GitHub handoff

Consolidate duplicates and return these sections in order. Post them as one GitHub PR comment only when currently authorized; reviewer subagents never post it themselves:

1. **Evidence considered** — description, base comparison, changed paths, applicable guidance, and CI state.
2. **Confirmed defects** — only reproducible or strongly evidenced defects, ordered `P0` through `P3`; each has `path:line`, impact, triggering condition, and reasoning. Write `None` when empty.
3. **Suggestions** — optional, non-blocking improvements. Write `None` when empty.
4. **Unanswered questions** — intent or evidence gaps that prevent a confident conclusion. Write `None` when empty.

Use authorized GitHub PR comments as durable shared context; do not assume automatic delivery to another Codex task.

## Decision

End the comment with exactly one final decision line and no other decision label:

- If any confirmed defect needs remediation, required CI evidence is not passing/available, or material intent/knowledge reconciliation is unresolved, add an **Implementation handoff prompt** immediately before the final line. It must be short, English, copy-pasteable into the original feature Codex task, cover every blocking finding, and ask it to update the same PR without merging and re-run proportionate validation. Distinguish queued/unavailable checks from failed checks or code defects. Final line: `CHANGES REQUIRED`.
- If no confirmed defects or material unresolved questions remain and required CI checks pass, final line: `READY FOR OWNER MERGE`.

Never merge, push fixes, deploy, change repository settings, invoke an LLM API, access secrets, credentials, ignored configuration, or GitHub Actions secrets. The repository owner manually merges only after `READY FOR OWNER MERGE` and the required CI checks pass. For an additional GitHub-hosted Codex review, comment `@codex review`; automatic Codex GitHub reviews are configured outside the repository in the Codex/GitHub integration.
