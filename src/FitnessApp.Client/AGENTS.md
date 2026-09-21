# Client guidance

This project is the Blazor WebAssembly UI. It owns pages, layouts, browser-facing state, API clients, and static assets; it references Contracts but never Server implementation projects.

- Keep UI text Danish and reuse the existing CSS tokens, shared components, and interaction patterns before adding new primitives.
- Send transport DTOs through the established same-origin HTTP clients. Keep access tokens memory-only and let the server decide identity, ownership, and authorization.
- Cover loading, empty, validation, success, denied, and recoverable-error states when the changed flow needs them.
- Read [client design evidence](docs/design-reference.md) for visual work and [shared product facts](../../docs/project.md) for behavior and invariants. Use `$fitness-design-check` for UI verification and `$fitness-feature` for end-to-end work.
