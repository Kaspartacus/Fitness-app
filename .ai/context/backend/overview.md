# Backend context

Server maps API endpoints, validates input and claims, and composes Application contracts with Infrastructure implementations. Application owns use-case contracts; Domain owns dependency-free business rules; Infrastructure owns EF Core, Identity, SQLite, email, and other external implementations. Contracts contains Client/Server transport DTOs.

For a backend task, inspect the relevant nested `AGENTS.md`, endpoint, service interface/implementation, tests, and migration. `tests/FitnessApp.IntegrationTests` exercises hosted APIs with isolated real SQLite. Use [data context](../data.md) when ownership, persistence, or recorded history is involved. Exact route and feature behavior comes from code and code-adjacent docs.
