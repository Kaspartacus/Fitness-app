# Repository instructions

## Working agreement

- Read [docs/project.md](docs/project.md) and [docs/progress.md](docs/progress.md) before implementation work.
- Work in small, reviewable vertical slices. Preserve unrelated changes and do not push, merge, deploy, or discard user work unless explicitly requested.
- Keep code, identifiers, technical documentation, and concise comments in English. Keep all application UI in Danish.
- Prefer explicit code and shared styling. Introduce reusable components only for a concrete reuse case, and remove superseded code in the active scope.
- Do not introduce speculative abstractions, generic repositories, MediatR, CQRS frameworks, message buses, microservices, giant services, or unnecessary interfaces.
- Suggest UX scope expansions before implementing them.

## Architecture

- Target C# and .NET 10. The browser application must run as Blazor WebAssembly, hosted from the ASP.NET Core server on the same origin.
- Maintain the dependency direction described in [docs/project.md](docs/project.md): Client over HTTP; Server to Application and Infrastructure; Application to Domain; Infrastructure to Application and Domain; Domain to no other solution project.
- A Server-to-Client project reference is allowed only for static WebAssembly hosting/build integration. API implementation must not depend on client types.
- Keep feature responsibilities separated within the six existing `src/` projects, including the already established Contracts project. Do not create empty future modules or new projects without a concrete need.
- Avoid paid services and platform-specific dependencies that prevent future Linux/ARM64 deployment.

## Security and data rules

- Treat all protected data as user-owned. Derive identity on the server and never accept a client-supplied user ID as proof of ownership.
- New accounts must await administrator approval. Pending or rejected accounts cannot access protected APIs or personal data, and registration must never permit choosing an administrator role.
- Use established password hashing, secure reset flows, HTTPS, safe logging, and production-safe errors when authentication is implemented.
- Never commit secrets or expose the future database publicly. Keep safe shared settings trackable and local secret overrides ignored.
- Preserve historical nutrition, completed activity, actual running, and weigh-in values when source definitions, plans, or goals change. See [docs/project.md](docs/project.md) for the full product invariants.

## Design and verification

- Follow the design workflow in [docs/project.md](docs/project.md) and [docs/design-reference.md](docs/design-reference.md) before each feature. Inspect the relevant current evidence and existing code, surface missing behavior and unresolved decisions, and implement agreed functionality end to end. Distinguish direct visual verification, consistent extensions, and unavailable evidence. Visual similarity alone is not feature completion.
- Reuse existing components and styling, remove superseded code in the active scope, and never change the Figma file unless explicitly authorized.
- Before handoff, run `./scripts/verify.sh verify`, applicable non-tautological tests not already covered there, `git status`, and a final diff review. Run `./scripts/verify.sh audit` when dependencies change or current advisory evidence is required.
- Start the server and verify the page plus WebAssembly assets in a browser when browser tooling is available. Record exact evidence and blockers in [docs/progress.md](docs/progress.md).

## Branches, review, and resume

- Use focused branches and pull requests for reviewable work. Commit, push, merge, deployment, and Figma changes require the user's current authorization; prior workflow permission is not permanent authority.
- Run a separate correctness review for substantial changes. Also run a security review when authentication, roles, authorization, ownership, secrets, logging, dependencies, configuration, or deployment changes. AI review supplements rather than replaces human approval.
- Keep `.codex/checkpoint.md` current at meaningful milestones with the objective, branch and last commit, completed and uncommitted work, verification, blockers, and exact next action. On resume, verify Git and filesystem state rather than trusting the checkpoint alone.
- Keep `AGENTS.md` for durable rules, `docs/project.md` for product and architecture, and `docs/progress.md` for current implementation evidence and next work. See [docs/development-workflow.md](docs/development-workflow.md) for tools and activation details.
