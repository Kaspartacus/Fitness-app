# FitnessApp guidance

FitnessApp is one .NET 10 hosted Blazor WebAssembly modular monolith. Client uses Contracts; Server alone hosts Application and Infrastructure, which depend on Domain. Extend `FitnessApp.slnx` and its six `src/` projects; do not add solutions, hosts, or ordinary feature worktrees.

## Start narrowly

Start Codex in the inner `Desktop/Fitness app/Fitness-app` Git root. Confirm `git rev-parse --show-toplevel`; preserve unrelated work. Read only:

- The nested `AGENTS.md` for the changed project or test directory.
- For non-trivial work: vault `personal`, `10 Projekter/Fitness App/00 Projektoversigt.md`, then the relevant module note. Consult `20 Arkitektur/Kodekort-og-API.md` to locate code; verify paths with `rg`. Prefer the Obsidian connector; otherwise try permitted access to `/Users/kaspartacuzz/Desktop/Kaspers Vault`. Never search unrelated personal folders.
- The current stage's skill in `.agents/skills/`; load supporting skills when needed, not the whole catalog.

## Project knowledge loop

Obsidian owns durable intent, decisions, architecture, behavior explanations, and limits. Code, configuration, migrations, and tests evidence implementation. Investigate disagreements.

- For every authorized change, including fixes, refactors, deletions, and tooling, assess the final diff's knowledge impact. Automatically update affected existing notes for changed durable facts or relevant stale guidance. Create notes only for new knowledge, link them from the overview, and retain one authoritative explanation. No cosmetic/no-impact rewrites.
- Read before editing; preserve unrelated content and use revision guards. Cite source paths and branch-specific commit/PR evidence; never describe unmerged/unverified work as main. Re-read for accuracy and check links.
- In the handoff and authorized PR's **Project knowledge**, report `updated` (paths/scope), `not needed` (reason), or `pending` (missing access/evidence and intended correction). Carry pending work forward until reconciled; never claim synchronization prematurely.
- If connector and permitted local access fail, use repository evidence and sanitized PR context. Ask about missing intent affecting product, security, or data. Cloud/GitHub agents cannot assume vault access: hand off specific pending corrections without copying private notes into Git/GitHub.
- Read-only reviews report discrepancies without vault edits. No background watcher: human edits need later reconciliation. Temporary task state belongs in ignored `.codex/checkpoint.md`.

## Working constraints

- English code/technical documentation; Danish application UI.
- Derive ownership/roles from validated server identity, never submitted values. Preserve recorded history when definitions change.
- Never expose secrets, credentials, database contents, or real personal/account/fitness data in any output or artifact. Use synthetic examples; never inspect secret values to check presence.
- Small, concrete changes only. No speculative abstractions, generic repositories, MediatR, CQRS frameworks, message buses, microservices, or dependencies without concrete need and current authorization.
- Commit, push, create PRs, merge, deploy, change Figma, or discard work only with current authorization. Never force-push/reset to simplify history.

## Local maps and procedures

- `.agents/skills/` owns procedures; `.codex/agents/` owns specialist roles. Use read-only `reviewer` for substantial diffs; add `security-reviewer` for authentication, authorization, ownership, private-data collection/export/retention/exposure, secrets, logging, dependencies, configuration, or deployment.
- Use `./scripts/verify.sh` proportionately. Markdown-only changes need static checks, not app/build execution. Current task restrictions override skill defaults; record skipped checks and leave CI enabled.

When the entire user message is exactly `Sæt i gang`, use `cleanup_maintainer` and `$fitness-cleanup-maintenance`; this authorizes only that workflow's branch, commit, push, and PR—not merge or deployment.

Every feature PR follows `$fitness-pr-review`. After each agent-initiated PR push, `$fitness-pr` posts one `@codex review` trigger for that head. Report only demonstrated consequential defects; CI owns deterministic checks.
