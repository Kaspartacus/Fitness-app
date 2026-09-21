# Domain guidance

This project contains dependency-free business types and rules for FitnessApp. It has no solution-project references.

- Keep it independent of HTTP, UI frameworks, EF Core, Identity, persistence, and infrastructure configuration.
- Put durable domain validation and history-preserving rules here only when they are independent of transport and storage.
- Avoid framework-shaped entities or speculative domain abstractions.
- Consult [product invariants](../../docs/project.md) before changing stored-business meaning. `$fitness-feature` is the shared procedure for a complete change.
