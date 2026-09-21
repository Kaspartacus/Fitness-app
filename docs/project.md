# Project overview

## Product direction (planned)

FitnessApp is a private, mobile-first fitness application, initially for two people. Its [source repository is intentionally public](https://github.com/Kaspartacus/Fitness-app), so anyone may inspect the source code. “Private application” means that protected functionality and personal data require authentication and administrator-approved access; it does not describe repository visibility.

The application is intended to run eventually on a Raspberry Pi through Docker and be reachable securely at home and remotely. Local hosting does not imply offline editing or synchronization.

The product modules are Home, Nutrition, Strength Training, Calendar, Weight Goals, Users, and Profile/Settings. They are delivered as small vertical slices rather than pre-created empty modules. Running, Strength Training, and Calendar are implemented within the same application, not separate products or hosts.

Visual implementation evidence and its limits are owned by the Client in [its design reference](../src/FitnessApp.Client/docs/design-reference.md). Repository development and review mechanics live in [development-workflow.md](development-workflow.md); they are not product requirements.

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
- A neutral Danish password-reset flow for Approved accounts using Identity's dedicated one-hour reset tokens, a trusted configured HTTPS origin, persisted cooldown, IP rate limiting, bounded asynchronous email delivery, and atomic all-session revocation.
- Configurable Brevo SMTP delivery through MailKit with required STARTTLS and a Development/Test-only private pickup transport. An explicit local Development opt-in can skip revocation checking for macOS compatibility; startup and actual listener checks prevent using it on nonlocal or non-Development instances, and all other certificate checks remain enabled. SMTP secrets remain server-side and real delivery is a separate manual check.
- An explicit `FitnessApp` Data Protection application identity with a persistent, private key-ring path so normal restarts preserve reset-token validity.
- Private strength programs for Approved users, with ordered workouts and exercises, planned weight, sets, repetitions and optional notes; weekly scheduling; actual completed-workout history; ownership isolation; and optimistic concurrency for program changes. Warm-up is an ordinary exercise rather than a stored boolean.
- Private running plans, dated scheduled sessions, and corrected actual results for Approved users. Setup captures current level, 30-minute ability, target distance/date, weekly frequency, and preferred weekdays; local deterministic generation uses `DateOnly` calendar dates derived from `TimeProvider` in `Europe/Copenhagen`, with a 10% weekly capability feasibility gate. Plans, sessions, and results remain owner-scoped, planned targets remain separate from actual measurements, completion is idempotent, and an explicit versioned replacement retains prior completed/manual history.
- The private `/kalender` route is the shared calendar: it presents active planned running sessions, actual manual and retired-plan running results, current/future strength-schedule occurrences, and actual completed strength snapshots. Running results render on their actual `DateOnly` date and appear only as a non-counting relationship on a different planned date. Strength has only a weekly schedule model, so past schedule occurrences are never manufactured; completed strength history uses the persisted Copenhagen-local completion day. Calendar reads are owner-scoped, inclusive, and bounded to 93 days.

## Architecture

FitnessApp is a modular monolith with layered responsibilities:

```text
Client --HTTP contracts--> Server --> Application --> Domain
        \-> Contracts <-----/          ^
Server ------------------------------> Infrastructure --> Domain
Server --static hosting/build only--> Client
```

`FitnessApp.slnx` is the only application solution. `FitnessApp.Server` is the only hosted startup project, launched with `dotnet run --project src/FitnessApp.Server`; it hosts both the Blazor WebAssembly client and ASP.NET Core API. Strength-program management and running are implemented within these existing layers and projects, with no separate fitness-feature solution, application, executable, host, or startup process.

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
- Strength templates, scheduling, and completed workouts are separate. Program exercises may contain a planned weight; the actual lifted weight belongs to performed workouts and remains separate from the plan. Active workouts allow direct editing and completion checkboxes and show previous performance; no rest timer or set-by-set wizard is planned.
- Running accepts a current level, a 0.5–15 km 30-minute ability distance, a 1–100 km target, a target date 7–365 Copenhagen-local days ahead, and one to seven distinct preferred weekdays. `DateOnly` scheduled dates are derived from `TimeProvider` in `Europe/Copenhagen`; deterministic generation schedules every selected weekday through the target period, tests the goal against the final selected session's capacity, and caps compounded weekly capacity growth at 10%. It rejects an infeasible target rather than promising an unsafe plan. An active plan can be replaced only with explicit confirmation and its matching version; retirement preserves its sessions and completed/manual history. The weekly-day planner changes days at the plan's current weekly frequency in place: historical, active, and result-linked sessions remain intact while only future unstarted sessions are regenerated. Each plan permits one started, uncompleted session at a time. Manual registration and correction require an actual distance and duration and cannot be pre-registered for a future calendar date. Planned completion and correction may have no measurements, can be registered before or after their scheduled day, and retain a separate actual date from 2000 through Copenhagen-local today. Planned and actual values remain separate; early active starts are still not allowed. An active timer derives elapsed time from its recorded server start source, never simulated live metrics. GPS, maps, background tracking, Garmin integration, and fabricated live pace/distance are not planned.
- Calendar combines strength and running. Planned dates and actual completion dates remain distinct; completed running and strength history remains stable when plans or programs change. Strength’s weekly schedule is only projected from the Copenhagen-local current day onward, never backwards into invented history.
- Profile contains settings and logout. Profile subpages do not repeat logout.

## Deferred decisions and scope

SQLite, Identity, JWT access tokens, registration, administrator approval, password reset, strength-program management, and the running vertical slice are implemented within the existing projects. Running migration `20260910221108_AddRunningModule` is generated and reviewed but has not been applied to any database. Email ownership verification remains operationally incomplete. Refresh tokens, remember-me behavior, production signing-key rotation, Docker/Raspberry Pi deployment, remote-access design, Garmin integration, deployment automation, vault selection, log shipping, and the remaining fitness product modules remain deferred.

Paid infrastructure and paid SaaS dependencies are out of scope. Future choices must remain compatible with Linux/ARM64 unless a documented decision changes that constraint.

CI/CD secrets must use GitHub Actions Secrets when workflows are introduced. Raspberry Pi runtime secrets require separate provisioning. A genuinely no-license-cost vault may be evaluated together with its operating burden; no hosted service, including Azure Key Vault, is assumed to remain permanently free. Private Grafana/Loki with bounded retention is a future observability candidate, not part of the current implementation.
