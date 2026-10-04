# FitnessApp context router

For non-trivial work, first read `10 Projekter/Fitness App/00 Projektoversigt.md` in the private Obsidian `personal` vault, then only notes relevant to the task. The local vault path is `~/Desktop/Kaspers Vault`; use Obsidian MCP when available. This folder holds concise recurring implementation context and a fallback when vault access fails. Inspect source, tests, project files, and migrations for exact current behavior; report unavailable vault access rather than silently skipping it.

- [Architecture](architecture.md): solution and responsibility boundaries.
- [Data](data.md): persistence, ownership, history, and local checkout behavior.
- [Backend](backend/overview.md): API, application services, infrastructure, and integration tests.
- [Frontend](frontend/overview.md): Blazor Client and design evidence.

For detailed feature behavior, navigate from the relevant source directory, nested `AGENTS.md`, and code-adjacent docs such as `docs/nutrition.md`. Add focused context notes only when a durable, frequently needed fact is otherwise hard to find.
