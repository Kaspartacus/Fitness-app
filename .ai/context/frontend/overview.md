# Frontend context

The Blazor WebAssembly Client owns pages, layouts, browser state, same-origin API calls, and static assets. It references Contracts, not Server internals. UI text is Danish while code and technical documentation are English. Reuse existing CSS tokens and shared components. Access tokens remain memory-only; Server decides identity and authorization.

For UI changes, inspect the relevant Client nested `AGENTS.md`, components, HTTP client, and [design evidence](../../../src/FitnessApp.Client/docs/design-reference.md). Verify changed states and mobile/desktop behavior when relevant; do not infer product decisions from visual similarity alone.
