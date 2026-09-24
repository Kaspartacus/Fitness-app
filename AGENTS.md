# FitnessApp guidance

FitnessApp is a private, mobile-first fitness application in a public repository. It is a .NET 10 modular monolith: the Blazor WebAssembly Client calls the ASP.NET Core Server over HTTP; Server composes Application and Infrastructure; Application and Infrastructure depend on Domain; Contracts contains shared transport DTOs. The Server's Client reference is only for static WebAssembly hosting.

Read this file, then only the guidance and documentation relevant to the task. Do not preload the whole documentation tree or unrelated module instructions.

## Working conventions

- Keep code, identifiers, technical documentation, and comments in English; keep application UI in Danish.
- Preserve unrelated work. Do not discard, push, merge, deploy, change Figma, or add dependencies without the user's current authorization.
- Keep implementation small and concrete. Do not add speculative projects, modules, abstractions, generic repositories, MediatR, CQRS frameworks, message buses, or microservices.
- Protected data is owner-scoped from the server-derived identity. Clients never establish ownership or roles with submitted IDs. Preserve historical recorded values when plans, goals, or definitions change.
- Keep secrets out of Git, database files out of `wwwroot`, and dependencies compatible with future Linux/ARM64 deployment. See [project facts and invariants](docs/project.md) for the authoritative detail.
- Remove superseded code, tests, configuration, generated output, and temporary artifacts in the changed scope.

## Instruction map

- [Client](src/FitnessApp.Client/AGENTS.md): Blazor UI, browser calls, and design evidence.
- [Server](src/FitnessApp.Server/AGENTS.md): HTTP API, authentication boundary, and composition root.
- [Application](src/FitnessApp.Application/AGENTS.md): use-case contracts and orchestration boundaries.
- [Domain](src/FitnessApp.Domain/AGENTS.md): dependency-free business rules.
- [Infrastructure](src/FitnessApp.Infrastructure/AGENTS.md): Identity, EF Core, SQLite, and external implementations.
- [Contracts](src/FitnessApp.Contracts/AGENTS.md): shared transport DTOs.
- [Integration tests](tests/FitnessApp.IntegrationTests/AGENTS.md): real-SQLite end-to-end coverage.

## Shared references

- [Project facts, architecture, and product invariants](docs/project.md)
- [Development workflow](docs/development-workflow.md), including verification, reviews, hooks, and pull requests
- [Repository skills](.agents/skills/): reusable feature, design-check, review, and pull-request procedures

## Exact cleanup trigger

- When the entire user message is exactly `Sæt i gang`, use the `cleanup_maintainer` custom agent and `$fitness-cleanup-maintenance` skill. This exact trigger authorizes that skill's cleanup branch, commit, push, and pull-request workflow, but never a merge or deployment.
- Do not treat `Sæt i gang` inside a longer message as a cleanup trigger.

## Code Review Rules

- Every feature PR follows the read-only procedure in `.agents/skills/fitness-pr-review/SKILL.md`; its GitHub comment is a handoff, not an approval or merge action.
- On every automatic review run or `@codex review` request, review the PR's latest head and post one new top-level GitHub comment that follows that procedure. Do not edit or rely on an earlier review comment.
- The implementing agent invokes that external review by following `$fitness-pr`: it posts one `@codex review` trigger after each agent-initiated PR push, using the PR's Review handoff focus. It never starts or messages another local Codex thread directly.
- Report only demonstrated consequential defects with a file reference and triggering condition. Leave deterministic formatting, build, test, and dependency checks to CI.
