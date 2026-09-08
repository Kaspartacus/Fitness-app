# Project overview

## Product direction (planned)

FitnessApp is a private, mobile-first fitness application, initially for two people. Its [source repository is intentionally public](https://github.com/Kaspartacus/Fitness-app), so anyone may inspect the source code. “Private application” means that protected functionality and personal data require authentication and administrator-approved access; it does not describe repository visibility.

The application is intended to run eventually on a Raspberry Pi through Docker and be reachable securely at home and remotely. Local hosting does not imply offline editing or synchronization.

Planned modules are Home, Nutrition, Strength Training, Running, Calendar, Weight Goals, Users, and Profile/Settings. They will be delivered as small vertical slices rather than pre-created empty modules.

The visual reference is the [current FitnessApp Figma Make file](https://www.figma.com/make/dFJcR42XWiqVhBtA1bfyOS/Fitness-app?p=f&t=3UwAWBKvTu85DryU-0) and its [published prototype](https://trance-vine-53032594.figma.site/). It illustrates approximately 90% of the intended functionality; this is design coverage, not implementation progress. It is not a complete functional specification: some controls are nonfunctional, and some flows, states, and requirements are absent. React source returned by Figma is reference material only and must be translated into Blazor.

Before implementing a feature, inspect its relevant design and existing code. Identify intended actions, validation, persistence, authorization, loading and empty states, errors, and success feedback. Surface unresolved product decisions and suggest useful small improvements; obtain agreement before material scope additions. Implement agreed behavior end to end through the relevant UI, API, business logic, and persistence layers. Do not treat visual matching as completion or silently reproduce nonfunctional prototype behavior. Reuse existing components and styling, remove superseded code within the active scope, and keep implemented behavior clearly separated from planned behavior.

The concise reusable visual facts and evidence classifications live in [design-reference.md](design-reference.md). Repository development and review mechanics live in [development-workflow.md](development-workflow.md); they are not product requirements.

For the registration slice, Figma Make version 41 replaced the former mint/purple direction with charcoal and gray surfaces, filled navy primary actions, light-blue accent/focus states, and readable gray-blue inactive states. The Blazor CSS now centralizes that translated palette and applies it consistently to login, registration, confirmation, protected home, and administrator review. The Make source inventory contains `LoginScreen.tsx` but no registration, confirmation, or administrator-review screens, so those states remain consistent extensions rather than pixel-verified matches. The current Make canvas preview failed to load during verification; exact pixel fidelity is therefore not claimed, but the current source inventory and visible version-41 design notes were successfully inspected.

## Implemented now

- A .NET 10 solution in `FitnessApp.slnx` with six projects under `src/` and an integration-test project under `tests/`.
- A standalone Blazor WebAssembly client served by the ASP.NET Core server from the same origin.
- EF Core SQLite persistence in Infrastructure with the initial Identity/session migration; database files live outside `wwwroot` and are ignored by Git.
- ASP.NET Core Identity users, password hashing and policy, `Admin` and `User` roles, lockout, and `Pending`, `Approved`, and `Rejected` account states.
- An explicit local command that creates the first Approved administrator from User Secrets once, refuses existing-account elevation, and is safe to rerun.
- Signed HS256 JWT login through ASP.NET Core JwtBearer, approximately 15-minute access tokens, strict issuer/audience/algorithm/signature/expiry checks, login rate limiting, and generic Danish failures.
- Persisted sessions plus live approval and role checks on protected requests, so logout, approval revocation, and role revocation take effect for an existing access token immediately.
- A memory-only same-origin API token client, Danish login form, protected home page with recoverable-load retry, and local logout with an explicit warning when server revocation cannot be confirmed. External 401 responses neither receive the token nor clear the application session. Reloading the browser deliberately requires login again.
- Public Danish registration with client/server validation, Identity password rules, neutral duplicate handling, duplicate submission protection, a separate rate limit, and creation of only a `Pending` account with the `User` role and no login session.
- Administrator-only pending-registration navigation and a bounded review API/UI with local timestamp display, explicit rejection confirmation, retryable failures, atomic approval/rejection, stale-decision conflicts, and persisted UTC decision metadata.
- A second EF Core migration adding nullable registration/decision metadata without inventing historical values for existing users.
- Structured JSON console logging for login, lockout, logout, registration, administrator decisions, and unexpected errors without credential, token, request-body, email, or display-name payloads.
- A lightweight pull-request workflow for restore, build, and tests with read-only permissions and no deployment or production secrets.
- Real SQLite integration coverage for authentication, bootstrap, registration, administration, concurrency, and empty/upgrade migration cases.

## Architecture

FitnessApp is a modular monolith with layered responsibilities:

```text
Client --HTTP contracts--> Server --> Application --> Domain
        \-> Contracts <-----/          ^
Server ------------------------------> Infrastructure --> Domain
Server --static hosting/build only--> Client
```

- `FitnessApp.Client` contains Blazor WebAssembly UI and does not reference server implementation projects.
- `FitnessApp.Server` is the ASP.NET Core host, authentication API, and composition root. It references Application and Infrastructure. Its Client reference exists solely to include static WebAssembly assets.
- `FitnessApp.Contracts` contains the concrete login/current-user transport DTOs shared by Client and Server; it contains no persistence types.
- `FitnessApp.Application` contains authentication service contracts and references Domain.
- `FitnessApp.Domain` contains business rules and domain types, with no dependency on EF Core, UI frameworks, or another solution project.
- `FitnessApp.Infrastructure` contains Identity and EF Core SQLite persistence and references Application and Domain.

Persistence entities are never shared with the client.

## Product invariants (planned)

- Registration is allowed, but every new account awaits administrator approval. Pending or rejected users cannot access protected APIs or personal data. Registrants cannot choose an administrator role.
- Personal data is isolated by server-derived identity; client-supplied user IDs never establish ownership.
- Nutrition is day-based with six fixed meal sections, including three distinct snack slots. Food values are per 100 g and entries use grams. Historical entries retain their original nutrition values.
- Strength templates, scheduling, and completed workouts are separate. Weight belongs to performed workouts. Active workouts allow direct editing and completion checkboxes and show previous performance; no rest timer or set-by-set wizard is planned.
- Running accepts custom distances, a target date, and one to seven preferred weekdays. Plans cover the full target period. Completion is manual through “Registrer løbetur”; planned and actual values remain separate. Live GPS is not planned.
- Calendar combines strength and running. Completed history and weigh-ins remain stable when plans or goals change.
- Profile contains settings and logout. Profile subpages do not repeat logout.

## Deferred decisions and scope

SQLite, Identity, JWT access tokens, registration, and administrator approval are implemented. Email ownership verification and password-reset delivery still require product and operational decisions. Refresh tokens, remember-me behavior, production signing-key rotation, Docker/Raspberry Pi deployment, remote-access design, Garmin integration, deployment automation, vault selection, log shipping, and all fitness product modules remain deferred.

Paid infrastructure and paid SaaS dependencies are out of scope. Future choices must remain compatible with Linux/ARM64 unless a documented decision changes that constraint.

CI/CD secrets must use GitHub Actions Secrets when workflows are introduced. Raspberry Pi runtime secrets require separate provisioning. A genuinely no-license-cost vault may be evaluated together with its operating burden; no hosted service, including Azure Key Vault, is assumed to remain permanently free. Private Grafana/Loki with bounded retention is a future observability candidate, not part of the current implementation.
