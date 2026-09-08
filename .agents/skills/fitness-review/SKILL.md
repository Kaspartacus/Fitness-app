---
name: fitness-review
description: Review a FitnessApp branch or diff for concrete correctness, regression, authorization, data-integrity, concurrency, failure-handling, test, and architecture risks. Use when a code-review pass is requested or required before handoff. Do not use to implement features or manage pull requests.
---

# Fitness Review

Perform an evidence-based review. Prioritize defects that can change behavior, security, or stored history.

## Inputs

- The requested review range or the current branch diff against its actual base.
- `AGENTS.md`, product invariants in `docs/project.md`, current status in `docs/progress.md`, and relevant tests.
- Verification output when available.

## Workflow

1. Confirm the branch, base, working-tree state, and exact diff under review.
2. Trace changed behavior end to end instead of reviewing files in isolation.
3. Check correctness, regressions, authorization and ownership, data integrity and history preservation, races and duplicates, failure paths, tests, and dependency direction.
4. For each defect, provide a tight file/line reference, impact, triggering conditions, and a practical reproduction or reasoning chain.
5. Distinguish:
   - **Defects:** actionable issues introduced or exposed by the change.
   - **Questions:** missing intent that prevents a confident conclusion.
   - **Residual risks:** limitations that are real but not necessarily defects in this scope.
6. Rank findings by impact. Do not manufacture a quota of findings.
7. If no actionable defects remain, say so and identify the most important unverified areas.

## Guardrails

- Remain read-only when invoked as a reviewer agent.
- Do not edit, commit, push, merge, approve, or resolve your own findings.
- Treat AI review as supplemental evidence, never as human approval.

## Completion

Return findings first, ordered by severity, followed by questions, residual risks, and the verification evidence considered. Use concise concrete references rather than a general summary.
