# Domain guidance

This project contains dependency-free business types and rules for FitnessApp. It has no solution-project references.

- Keep it independent of HTTP, UI frameworks, EF Core, Identity, persistence, and infrastructure configuration.
- Put durable domain validation and history-preserving rules here only when they are independent of transport and storage.
- Avoid framework-shaped entities or speculative domain abstractions.
- Before changing stored-business meaning, read the relevant `.ai/context` note and, when accessible, the relevant Fitness App vault note. `$fitness-feature` is the shared procedure for a complete change.
