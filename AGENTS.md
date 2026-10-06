# FitnessApp guidance

FitnessApp is one .NET 10 hosted Blazor WebAssembly modular monolith. The Client uses Contracts; Server is the only host and composes Application and Infrastructure; Application and Infrastructure depend on Domain. Extend the existing `FitnessApp.slnx` and six `src/` projects—never add another solution, app host, or ordinary feature worktree.

## Start narrowly

Start Codex in the Git root (locally `Desktop/Fitness app/Fitness-app`, not its outer container). Confirm `git rev-parse --show-toplevel` before editing and preserve unrelated work. Read only:

- The nested `AGENTS.md` for the changed project or test directory.
- For non-trivial work: Obsidian vault `personal`, beginning with `10 Projekter/Fitness App/00 Projektoversigt.md` and the relevant module note. Use `20 Arkitektur/Kodekort-og-API.md` when locating code, then confirm paths with `rg`. Prefer the configured Obsidian connector; if unavailable, check the known local vault `/Users/kaspartacuzz/Desktop/Kaspers Vault` using permitted file access before declaring it unavailable. Never search unrelated personal folders for a replacement.
- The relevant repository skill in `.agents/skills/` for the current stage; load supporting skills when reaching implementation, review, or PR stages, not the whole catalog up front.

## Project knowledge loop

Obsidian is the authoritative home for durable project intent, decisions, architecture, behavior explanations, and known limits. Code, configuration, migrations, and tests show what is implemented; investigate disagreements instead of assuming either side is correct.

- Every authorized change task (including fixes, refactors, deletions, and tooling) must assess knowledge impact against its final diff. Without a separate reminder, update affected existing notes when durable facts change or relevant stale guidance is discovered. Add a note only for genuinely new knowledge, link it from the overview, and keep one authoritative explanation. Do not rewrite notes for cosmetic/no-impact edits.
- Read before editing, preserve unrelated content, and use revision guards where supported. Record relevant source paths and the implementation commit/PR when a statement is branch-specific. Do not present unmerged or unverified work as the main baseline. Re-read edits and check links; do not assume a successful write proves factual accuracy.
- Report `updated` (note paths and scope), `not needed` (reason), or `pending` (exact missing access/evidence and intended correction) in the task handoff and, when authorized, the PR's **Project knowledge** section. Pending knowledge work must not be described as fully synchronized. Reconcile it when a later authorized task has access; do not silently drop it.
- If both connector and permitted local access fail, use repository evidence and the PR's sanitized context. Ask when missing intent would materially affect product, security, or data decisions. Cloud/GitHub agents must not assume access to the owner's local vault; hand off the specific pending note changes without copying private notes into Git or GitHub.
- Read-only reviews report discrepancies and do not edit the vault. This is an agent task workflow, not a background file watcher; it cannot detect arbitrary human edits without a later reconciliation task. Keep temporary task state in ignored `.codex/checkpoint.md`, not permanent project notes.

## Working constraints

- Keep code and technical documentation English; keep application UI Danish.
- Derive ownership and roles from validated server identity; never trust submitted identity or role values. Preserve recorded history when definitions change.
- Never expose or commit secrets, tokens, credentials, database contents, or real personal/account/fitness data in source, logs, tool output, documentation, notes, commits, or PRs. Use synthetic examples; do not inspect secret values to verify their presence.
- Keep changes small and concrete. Do not add speculative abstractions, generic repositories, MediatR, CQRS frameworks, message buses, microservices, or dependencies without a concrete need and current authorization.
- Commit, push, create PRs, merge, deploy, change Figma, or discard work only with current user authorization. Never force-push or reset to simplify history.

## Local maps and procedures

- [Client](src/FitnessApp.Client/AGENTS.md), [Server](src/FitnessApp.Server/AGENTS.md), [Application](src/FitnessApp.Application/AGENTS.md), [Domain](src/FitnessApp.Domain/AGENTS.md), [Infrastructure](src/FitnessApp.Infrastructure/AGENTS.md), [Contracts](src/FitnessApp.Contracts/AGENTS.md), and [integration tests](tests/FitnessApp.IntegrationTests/AGENTS.md).
- Reusable procedures live in `.agents/skills/`. Read-only review roles live in `.codex/agents/`; use them for substantial diffs, and add the security reviewer for authentication, authorization, ownership, private-data collection/export/retention/exposure, secrets, logging, dependencies, configuration, or deployment.
- Use `./scripts/verify.sh` for shared verification, proportionate to the changed files and current user constraints. Markdown-only changes need static checks, not an app run or build. Explicit task restrictions override skill defaults; record skipped checks honestly and leave CI enabled.

When the entire user message is exactly `Sæt i gang`, use `cleanup_maintainer` and `$fitness-cleanup-maintenance`; this authorizes only that workflow's branch, commit, push, and PR—not merge or deployment.

Every feature PR follows `$fitness-pr-review`. After each agent-initiated PR push, `$fitness-pr` posts one `@codex review` trigger for the latest head. Report only demonstrated consequential defects; CI owns deterministic checks.
