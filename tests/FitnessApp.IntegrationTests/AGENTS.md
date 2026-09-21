# Integration-test guidance

This project verifies FitnessApp through the hosted Server using isolated real SQLite databases and the shared test factory.

- Test observable API and persistence behavior, not implementation details. Cover success, denial, invalid input, race/duplicate behavior, and history preservation when relevant.
- Keep test setup isolated; do not depend on a developer database, external email provider, or a running application process.
- Update tests with the behavior they specify, and do not replace meaningful integration coverage with tautological unit tests.
- Read [the verification workflow](../../docs/development-workflow.md) and [product invariants](../../docs/project.md). Use `$fitness-review` when assessing test coverage for a substantial diff.
