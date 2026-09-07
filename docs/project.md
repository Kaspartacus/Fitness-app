# Project overview

## Product direction (planned)

FitnessApp is a private, mobile-first fitness application, initially for two people. Its [source repository is intentionally public](https://github.com/Kaspartacus/Fitness-app), so anyone may inspect the source code. “Private application” means that protected functionality and personal data require authentication and administrator-approved access; it does not describe repository visibility.

The application is intended to run eventually on a Raspberry Pi through Docker and be reachable securely at home and remotely. Local hosting does not imply offline editing or synchronization.

Planned modules are Home, Nutrition, Strength Training, Running, Calendar, Weight Goals, Users, and Profile/Settings. They will be delivered as small vertical slices rather than pre-created empty modules.

The visual reference is the [FitnessApp Figma Make file](https://www.figma.com/make/dFJcR42XWiqVhBtA1bfyOS/Fitness-app?t=hqsbdqLY14eE7ijT-0). It illustrates approximately 90% of the intended functionality; this is design coverage, not implementation progress. It is not a complete functional specification: some controls are nonfunctional, and some flows, states, and requirements are absent. React source returned by Figma is reference material only and must be translated into Blazor.

Before implementing a feature, inspect its relevant design and existing code. Identify intended actions, validation, persistence, authorization, loading and empty states, errors, and success feedback. Surface unresolved product decisions and suggest useful small improvements; obtain agreement before material scope additions. Implement agreed behavior end to end through the relevant UI, API, business logic, and persistence layers. Do not treat visual matching as completion or silently reproduce nonfunctional prototype behavior. Reuse existing components and styling, remove superseded code within the active scope, and keep implemented behavior clearly separated from planned behavior.

Detailed visual work requires direct Figma access or screenshots/exports supplied by the user. Until then, do not claim visual fidelity. The intended visual direction is charcoal backgrounds, elevated dark cards, off-white text, muted secondary text, a mint/teal accent, accessible labels, visible keyboard focus, and large touch targets.

## Implemented now

- A .NET 10 solution in `FitnessApp.slnx` with five projects under `src/`.
- A standalone Blazor WebAssembly client served by the ASP.NET Core server from the same origin.
- Empty Application, Domain, and Infrastructure layers with only the required project references.
- A minimal Danish foundation page. No product module, API, persistence, authentication, or deployment implementation exists yet.

## Architecture

FitnessApp is a modular monolith with layered responsibilities:

```text
Client --HTTP--> Server --> Application --> Domain
                         \-> Infrastructure --> Application + Domain
Server --static hosting/build only--> Client
```

- `FitnessApp.Client` contains Blazor WebAssembly UI and does not reference server implementation projects.
- `FitnessApp.Server` is the ASP.NET Core host, future API, and composition root. It references Application and Infrastructure. Its Client reference exists solely to include static WebAssembly assets.
- `FitnessApp.Application` contains use cases and references Domain.
- `FitnessApp.Domain` contains business rules and domain types, with no dependency on EF Core, UI frameworks, or another solution project.
- `FitnessApp.Infrastructure` will contain persistence and external integrations and references Application and Domain.

Transport contracts will be added only when an actual client/server contract requires them. Persistence entities will never be shared with the client.

## Product invariants (planned)

- Registration is allowed, but every new account awaits administrator approval. Pending or rejected users cannot access protected APIs or personal data. Registrants cannot choose an administrator role.
- Personal data is isolated by server-derived identity; client-supplied user IDs never establish ownership.
- Nutrition is day-based with six fixed meal sections, including three distinct snack slots. Food values are per 100 g and entries use grams. Historical entries retain their original nutrition values.
- Strength templates, scheduling, and completed workouts are separate. Weight belongs to performed workouts. Active workouts allow direct editing and completion checkboxes and show previous performance; no rest timer or set-by-set wizard is planned.
- Running accepts custom distances, a target date, and one to seven preferred weekdays. Plans cover the full target period. Completion is manual through “Registrer løbetur”; planned and actual values remain separate. Live GPS is not planned.
- Calendar combines strength and running. Completed history and weigh-ins remain stable when plans or goals change.
- Profile contains settings and logout. Profile subpages do not repeat logout.

## Deferred decisions and scope

The database provider and authentication design are intentionally undecided. EF Core is intended but not installed. Database schema, administrator bootstrap, authorization, password reset, Docker/Raspberry Pi deployment, remote-access design, Garmin integration, CI, and all product features are deferred to later focused tasks.

Paid infrastructure and paid SaaS dependencies are out of scope. Future choices must remain compatible with Linux/ARM64 unless a documented decision changes that constraint.
