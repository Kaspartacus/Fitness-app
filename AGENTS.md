# FitnessApp guidance

FitnessApp is one .NET 10 hosted Blazor WebAssembly modular monolith. Client uses Contracts; Server alone hosts Application and Infrastructure, which depend on Domain. Extend `FitnessApp.slnx` and its six `src/` projects; do not add solutions, hosts, or ordinary feature worktrees.

## Start narrowly

Classify the actual change as small, standard, or large before broad discovery; state the choice briefly. Prompt length, urgency, and line count alone do not determine size.

- **Small:** one obvious, low-risk correction with known scope, normally 1–3 files: typo, label, broken documentation link, or mechanical local edit with no behavior change. No uncertain diagnosis, cross-layer behavior, API, persistence, security, dependency, deployment, or destructive-workflow change. **No subagents**, including delegated review. Read only affected guidance/files, inspect the final diff yourself, and run the smallest relevant checks. No broad exploration, formal plan, repeated reviews, or additional vault reads after initial context when knowledge impact is clearly absent.
- **Standard:** a bounded feature/fix or instruction change requiring reasoning beyond those limits. One primary agent by default; delegate one bounded review only for a concrete risk or useful independent check, explaining why. Inspect affected paths and relevant knowledge, not the full solution.
- **Large:** cross-module/architectural changes, migrations, complex security or concurrency work, or substantial uncertainty. The primary agent coordinates a short plan, one `implementer` executes, and a separate read-only reviewer checks the stable result. Follow `$fitness-feature` **Lean orchestration**; at most two subagents at once, no recursive delegation.

If a small task uncovers risk or expands, reclassify before proceeding; never split risky work artificially to retain small status. For PR review, classify the whole unreviewed diff, not only the latest edit. All sizes preserve safety, authorization, relevant verification, CI and knowledge-impact reporting; reuse valid evidence and stop when the requested scope is complete.

On Dev, start Codex in `/home/codex/projects/fitness-app` or the explicitly selected Fitness App worktree. Confirm `git rev-parse --show-toplevel`, branch and status; preserve unrelated work. Use the current worktree for commands, not a hard-coded checkout. Read only:

- The nested `AGENTS.md` for the changed project or test directory.
- When establishing Fitness App context, read `/home/codex/second-brain/AI-brugsregler.md`, `/home/codex/second-brain/Start.md`, and `/home/codex/second-brain/10 Projekter/Fitness App/00 Projektoversigt.md`. Follow these shared rules directly from the local Dev vault, available even when the Mac is off; do not depend on a connector or contact the Mac. Reuse already-read, still-current context within a session; small tasks with clearly absent knowledge impact need no additional vault reads.
- Before architectural answers or significant technical changes, use `30 Udvikling/Kontekstguide-for-Codex.md` under that project area to select relevant notes. Search that area with `rg` and follow relevant Obsidian links; read only matching product, architecture, decisions, status or module notes. Consult `20 Arkitektur/Kodekort-og-API.md` to locate code and verify paths against the current checkout. Never preload the entire vault or search unrelated personal notes.
- The current stage's skill in `.agents/skills/`; load supporting skills when needed, not the whole catalog.

## Project knowledge loop

Obsidian owns durable intent, decisions, architecture, behavior explanations, and limits. Code, configuration, migrations, and tests are authoritative for actual implementation; outdated notes never override verified source. Investigate disagreements without silently turning code defects into requirements. Shared documentation lifecycle and privacy rules live in `/home/codex/second-brain/AI-brugsregler.md`; project-specific implementation procedures stay here in Git. Never copy the vault into Git/GitHub.

- For every authorized change, including fixes, refactors, deletions, and tooling, assess the final diff's knowledge impact. Automatically update affected existing notes for changed durable facts or relevant stale guidance. Create notes only for new knowledge, link them from the overview, and retain one authoritative explanation. No cosmetic/no-impact rewrites.
- Read before editing; preserve unrelated content and use revision guards. Cite source paths and branch-specific commit/PR evidence; never describe unmerged/unverified work as main. Re-read for accuracy and check links.
- In the handoff and authorized PR's **Project knowledge**, report `updated` (paths/scope), `not needed` (reason), or `pending` (missing access/evidence and intended correction). Carry pending work forward until reconciled; never claim synchronization prematurely.
- If connector and permitted local access fail, use repository evidence and sanitized PR context. Ask about missing intent affecting product, security, or data. Cloud/GitHub agents cannot assume vault access: hand off specific pending corrections without copying private notes into Git/GitHub.
- Read-only reviews report discrepancies without vault edits. No background watcher: human edits need later reconciliation. Temporary task state belongs in ignored `.codex/checkpoint.md`.

## Working constraints

- Owner deployment boundary: never merge feature branches into `main` on Dev or bypass the GitHub PR/review/human approval workflow. The existing dedicated clean main checkout may fast-forward to already-approved `origin/main` through its enabled synchronization timer; leave that host setup unchanged. Only `feature/*` may be manually deployed to staging on Dev; `main` is reserved for future automatic production on Apps. The owner requests full Docker access for Codex on Dev and accepts its root-equivalent authority; no separate approval broker is required. Always show the specific operation/commit/resources and obtain explicit human approval before every Docker mutation/deployment. Read-only status/logs need no additional approval. Repository implementation approval is not deployment approval. The CLI's interactive confirmation and these instructions are operator guardrails, not a technical security boundary against full Docker access. Do not grant Docker access or change host/certificate/firewall settings without separate authorization.

- English code/technical documentation; Danish application UI.
- Derive ownership/roles from validated server identity, never submitted values. Preserve recorded history when definitions change.
- Never expose secrets, credentials, database contents, or real personal/account/fitness data in any output or artifact. Use synthetic examples; never inspect secret values to check presence.
- Small, concrete changes only. No speculative abstractions, generic repositories, MediatR, CQRS frameworks, message buses, microservices, or dependencies without concrete need and current authorization.
- Commit, push, create PRs, merge, deploy, change Figma, or discard work only with current authorization. Never force-push/reset to simplify history.

## Local maps and procedures

- Use `feature/` for new implementation, `hotfix/` for urgent defects in main, and `bugfix/` for other unexpected behavior. Follow `$fitness-pr` **Branch lifecycle** for naming and required safe local/remote deletion after merge; never delete main or unfinished work.
- `.agents/skills/` owns procedures; `.codex/agents/` owns specialist roles. Apply task sizing to delegation. Changes affecting authentication, authorization, ownership, private-data handling, secrets, logging, dependencies, configuration, or deployment need a focused security check and are not small; use `security-reviewer` when independent review is warranted, not merely because a file mentions these topics.
- Use `./scripts/verify.sh` proportionately. Markdown-only changes need static checks, not app/build execution. Current task restrictions override skill defaults; record skipped checks and leave CI enabled.

When the entire user message is exactly `Sæt i gang`, use `cleanup_maintainer` and `$fitness-cleanup-maintenance`; this authorizes only that workflow's branch, commit, push, and PR—not merge or deployment.

Every feature PR follows `$fitness-pr-review` with task sizing. For standard/large unreviewed PR diffs or an explicit user request for AI review of that PR, `$fitness-pr` posts one `@codex review` trigger per new head after each agent-initiated push. The request covers later heads of the same PR unless withdrawn. Small-only diffs otherwise get self-review without automatic AI-review requests. CI stays enabled. Report only demonstrated consequential defects.
