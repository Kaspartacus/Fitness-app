# Infrastructure guidance

This project implements Application services with Identity, EF Core, SQLite, email delivery, and other external concerns. It references Application and Domain.

- Keep persistence entities and provider details inside Infrastructure; never expose them to Client or Contracts.
- Scope reads and writes to the owner supplied by the Server/Application boundary, preserve historical records, and maintain existing concurrency behavior.
- Keep databases and Data Protection material outside `wwwroot`; do not put connection strings or credentials in tracked settings. Add or update EF migrations only when the persisted model changes.
- Read [project facts](../../docs/project.md) for persistence and security invariants. Use `$fitness-feature` for a vertical slice and `$fitness-review` plus `security-reviewer` for sensitive changes.
