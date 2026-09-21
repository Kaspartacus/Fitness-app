# Application guidance

This project owns application-facing service contracts and models. It references Domain and defines the boundary that Infrastructure implements and Server consumes.

- Keep use-case orchestration contracts here and express business concepts with Domain types where appropriate.
- Do not introduce HTTP, UI, EF Core, Identity, SQLite, configuration, or infrastructure-provider dependencies.
- Keep interfaces concrete to an existing use case; do not add generic abstraction layers for future work.
- Read [project facts](../../docs/project.md) when a change affects a product invariant. `$fitness-feature` covers coordinated vertical-slice work.
