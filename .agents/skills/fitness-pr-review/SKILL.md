---
name: fitness-pr-review
description: Produce one human-gated, read-only GitHub pull-request review and a precise implementation handoff. Use for feature PR review after the branch is available; do not use to implement, push fixes, merge, deploy, or prepare a PR.
---

# Fitness PR Review

Publish actionable feedback for one feature PR while keeping all reviewers read-only. This procedure never merges, pushes fixes, deploys, changes repository settings, or reads secrets, credentials, ignored local configuration, or GitHub Actions secrets. It does not add or invoke a GitHub Action or LLM API.

## Evidence

1. Read the PR description, base and head refs, changed-file list, current CI status, and the diff against the PR base. Use GitHub's read-only PR views and local read-only Git commands; record unavailable evidence rather than guessing.
2. Read the root `AGENTS.md` and only the nested `AGENTS.md` files governing changed paths. Read the direct code and tests needed to understand each changed behavior.
3. Run the `reviewer` agent for source review. Run `security-reviewer` as well when the PR touches authentication, authorization, ownership, secrets, logging, dependencies, configuration, or deployment. Both agents remain read-only. A separate PR-explorer agent is unnecessary because this short evidence collection is the coordinator's responsibility.

## Findings and GitHub handoff

Consolidate duplicate findings and post one GitHub PR comment. Do not approve, request changes through a privileged action, or start a fix. Separate the comment into these sections in this order:

1. **Evidence considered:** PR description, base comparison, changed paths, applicable guidance, and current CI state.
2. **Confirmed defects:** only reproducible or strongly evidenced defects, ordered `P0` through `P3`. Each includes `path:line`, concise impact, triggering condition, and reasoning. Write `None` when empty.
3. **Suggestions:** optional, non-blocking improvements. Write `None` when empty.
4. **Unanswered questions:** intent or evidence gaps that prevent a confident conclusion. Write `None` when empty.

Use GitHub PR comments as the handoff between separate Codex threads. They are durable shared context; do not claim that one Codex thread can message another automatically.

## Decision

End the comment with exactly one of these lines and no other decision label:

- When one or more confirmed defects need remediation, include an **Implementation handoff prompt** before the final line. It must be short, in English, and copy-pasteable: `Update PR #<number> without merging. Fix <P# path:line> so that <expected behavior>. Re-run proportionate validation and report the result in this PR.` Include every blocking finding, then end with `Decision: CHANGES REQUIRED`.
- `Decision: READY FOR HUMAN MERGE` only when no confirmed defects remain and the CI evidence is satisfactory. This is not an approval or merge action.

Only a human may merge, and only after the required CI checks and review are satisfactory. For an additional GitHub-hosted Codex review, comment `@Codex review` on the PR. Automatic GitHub reviews are enabled outside the repository in Codex settings for the connected repository; never try to enable them by editing repository settings or workflows.
