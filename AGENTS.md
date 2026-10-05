# FitnessApp guidance

FitnessApp is one .NET 10 hosted Blazor WebAssembly modular monolith. The Client uses Contracts; Server is the only host and composes Application and Infrastructure; Application and Infrastructure depend on Domain. Extend the existing `FitnessApp.slnx` and six `src/` projects—never add another solution, app host, or ordinary feature worktree.

## Start narrowly

Confirm `git rev-parse --show-toplevel` before editing and preserve unrelated work. Read only:

- The nested `AGENTS.md` for the changed project or test directory.
- For non-trivial product, architecture, data, security, API, integration, or feature work: Obsidian vault `personal`, beginning with `10 Projekter/Fitness App/00 Projektoversigt.md`; use `20 Arkitektur/Kodekort-og-API.md` to locate the narrow code slice, then confirm paths with `rg`.
- The one relevant repository skill in `.agents/skills/` when its outcome matches the task.

The vault is durable project knowledge; code, configuration, migrations, and tests are executable truth. Investigate disagreements and update durable vault knowledge only when a task changes it. If vault access is unavailable, work from repository evidence and report the gap. Never put secrets or personal data in guidance or vault notes.

## Working constraints

- Keep code and technical documentation English; keep application UI Danish.
- Derive ownership and roles from validated server identity; never trust submitted identity or role values. Preserve recorded history when definitions change.
- Keep changes small and concrete. Do not add speculative abstractions, generic repositories, MediatR, CQRS frameworks, message buses, microservices, or dependencies without a concrete need and current authorization.
- Commit, push, create PRs, merge, deploy, change Figma, or discard work only with current user authorization. Never force-push or reset to simplify history.

## Local maps and procedures

- [Client](src/FitnessApp.Client/AGENTS.md), [Server](src/FitnessApp.Server/AGENTS.md), [Application](src/FitnessApp.Application/AGENTS.md), [Domain](src/FitnessApp.Domain/AGENTS.md), [Infrastructure](src/FitnessApp.Infrastructure/AGENTS.md), [Contracts](src/FitnessApp.Contracts/AGENTS.md), and [integration tests](tests/FitnessApp.IntegrationTests/AGENTS.md).
- Reusable procedures live in `.agents/skills/`; read only the applicable one. Read-only review roles live in `.codex/agents/`; use them for substantial diffs, and add the security reviewer for authentication, authorization, ownership, secrets, logging, dependencies, configuration, or deployment.
- Use `./scripts/verify.sh` for shared verification. Keep optional handoff state only in ignored `.codex/checkpoint.md`.

When the entire user message is exactly `Sæt i gang`, use `cleanup_maintainer` and `$fitness-cleanup-maintenance`; this authorizes only that workflow's branch, commit, push, and PR—not merge or deployment.

Every feature PR follows `$fitness-pr-review`. After each agent-initiated PR push, `$fitness-pr` posts one `@codex review` trigger for the latest head. Report only demonstrated consequential defects; CI owns deterministic checks.
