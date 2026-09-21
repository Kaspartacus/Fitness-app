# Server guidance

This is the ASP.NET Core host, API boundary, and composition root. It hosts the Client's static WebAssembly assets, maps HTTP endpoints, configures authentication and options, and wires Application contracts to Infrastructure implementations.

- Keep endpoint handling thin: validate transport input, derive the caller from claims, call the application service, and map safe HTTP responses.
- Require authorization at protected API boundaries. Derive owner and administrator identity from validated server claims; never accept submitted identity or role fields as proof.
- Do not move persistence or business rules into endpoints. The Client reference is only for static hosting/build integration, not for API implementation.
- Consult [project facts](../../docs/project.md) for shared security and product invariants. Use `$fitness-feature` for a slice and `$fitness-review` for a focused review; use the `security-reviewer` for security-sensitive diffs.
