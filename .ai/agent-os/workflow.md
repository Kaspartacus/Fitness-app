# Task workflow

This is Codex guidance, not a workflow engine. Use the smallest workflow that safely completes the user's request. The user can confirm or override any recommended complexity. Do not force a pause for routine choices.

## Intake and Task Contract

Classify each material statement as **FACT** (directly supported by user, code, or confirmed context), **INFERENCE** (strongly suggested), or **UNKNOWN** (unsupported). Draft, don't decide. Ask before assuming when an unknown can materially change product behavior, architecture, security, data, or scope; the human decides. Resolve routine implementation choices from the existing repository.

For non-trivial tasks, read the Fitness App vault overview and the relevant note before drafting the Task Contract. If vault access fails, use repository evidence and report that limitation. For normal or large tasks, retain a concise Task Contract in the working conversation or ignored `.codex/checkpoint.md` when handoff is needed: goal, confirmed requirements, scope limits, constraints, affected areas, open questions, ambiguity, complexity, and risk. For tiny clear tasks, one sentence is enough. Subsequent roles use this same interpretation.

Assess **ambiguity**, **complexity**, and **risk** separately. Ambiguity determines whether a material question must be answered. Complexity (small / normal / large) determines planning and review depth; present the recommendation and reason when useful, and accept a human override. Risk determines extra security review, validation, or approval even for a small change. See [model and effort routing](routing.md).

## Adaptive flow

- Small, clear, low risk: implement, run relevant deterministic checks, finalize.
- Normal: establish Task Contract, plan briefly, implement, validate, review the relevant diff, finalize.
- Large or high risk: clarify material unknowns, plan, implement in reviewable slices, validate, use independent review and focused tester/security roles when they add value, finalize. Seek human approval for high-impact actions when required.

Use a Developer ↔ Reviewer fix loop only for actionable findings. Re-run affected checks and review the complete relevant diff after a fix. After at most two re-review rounds, summarize remaining findings and ask the user to choose the next direction. Do not treat an agent's claim as validation evidence.

## State and Git

Confirm repository root, branch, status, and relevant worktree data before Git operations. Ordinary features use a branch in the existing checkout; name new feature branches `feature/<short-task-name>` when appropriate. Preserve the current branch and uncommitted work. Do not reset, force-push, discard data, or move a dirty checkout merely to simplify history. Commit/push/PR/merge/deploy only with current authorization; an explicit task instruction or exact cleanup trigger can provide that authorization. Never infer merge authorization from PR authorization.

Use the existing `./scripts/verify.sh verify` and, when dependency or release evidence matters, `./scripts/verify.sh audit`. Report exact command results and any checks not run. Keep optional task handoff state in ignored `.codex/checkpoint.md`. At finalization, update the affected vault note when durable project knowledge or technical procedures change. Keep `.ai/context` as a routing index, not a second knowledge store. Skip updates when the facts did not change. Full procedures live in vault note `30 Udvikling/Repository-workflow.md`.
