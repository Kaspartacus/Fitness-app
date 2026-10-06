# Infrastructure guidance

This project implements Application services with Identity, EF Core, SQLite, email delivery, and other external concerns. It references Application and Domain.

- Keep EF mappings, Identity entities, and provider-specific persistence models inside Infrastructure. Existing dependency-free Domain entities are mapped here; do not move them merely because they are persisted. Never expose persistence models through transport DTOs.
- Scope reads and writes to the owner supplied by the Server/Application boundary, preserve historical records, and maintain existing concurrency behavior.
- Keep databases and Data Protection material outside `wwwroot`; never track credentials or credential-bearing connection strings. The existing non-secret SQLite development path is configuration, not a secret. Add or update EF migrations only when the persisted model changes.
- For persistence and security invariants, read the relevant Fitness App vault note; report unavailable vault access. Use `$fitness-feature` for a vertical slice and `$fitness-review` plus `security-reviewer` for sensitive changes.
