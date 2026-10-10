# FitnessApp

FitnessApp is a private, mobile-first fitness application in an intentionally public source repository. Protected functionality requires an approved ASP.NET Core Identity account. The current vertical slices provide SQLite persistence, account administration and authentication, password reset by email, strength programs, running plans and results, a shared calendar, nutrition logging, and profile/settings through the Danish UI.

This README is the public developer setup and operational guide. The private `Kaspers Vault` Obsidian vault is the source of truth for Fitness App project documentation, including `30 Udvikling/Repository-workflow.md`, `30 Udvikling/Design-evidens.md`, and `20 Arkitektur/Nutrition-implementering.md`. [AGENTS.md](AGENTS.md) is the concise Codex entrypoint; code, configuration, migrations, and tests remain authoritative for implemented behavior.

## Prerequisites

- .NET SDK 10.0.103 or a later compatible .NET 10 feature-band SDK, as controlled by `global.json`
- OpenSSL for generating local signing material
- Python 3 for safely JSON-encoding the one-shot User Secrets input below
- A trusted ASP.NET Core development certificate for browser-based HTTPS testing

No Node.js runtime, container engine, or cloud account is required.

## Repository structure

The only solution file is `FitnessApp.slnx`. Run commands from the inner `Desktop/Fitness app/Fitness-app` repository directory; the outer `Fitness app` is its container. Other Git worktrees are separate checkouts of this same application, not separate solutions; their default local databases are separate, so do not use them as the ordinary startup folder.

In Codex, set this inner repository as the project's primary folder and start new tasks there. Changing a shell command's working directory does not retroactively load child skills or project configuration into an already-started task. Keep repository skills in `.agents/skills/`; do not copy them into the outer container. See [Codex project folders](https://learn.chatgpt.com/docs/projects) and [skill discovery](https://learn.chatgpt.com/docs/build-skills).

### AI workspace layout

The similarly named folders have different native Codex roles; they are not competing harnesses:

| Location | Responsibility |
| --- | --- |
| `AGENTS.md` and project/test-level `AGENTS.md` | Short shared rules and relevant layer constraints. |
| `.agents/skills/<skill>/SKILL.md` | Reusable, task-specific procedures, loaded when selected. |
| `.agents/skills/<skill>/agents/openai.yaml` | Skill UI metadata, not another executable agent. |
| `.codex/agents/*.toml` | Specialist definitions: cleanup maintainer, large-task implementer and read-only reviewers. |
| `.codex/hooks.json` and `.codex/hooks/` | Optional, explicitly trusted session-context hook. |
| Private Obsidian project notes | Durable decisions and project knowledge, not a duplicate procedure catalog. |

Keep both `.agents/skills/` and `.codex/agents/` in these supported locations. There is no active `.ai/` tree; its legacy guidance was consolidated into the native locations and the vault. Git does not track empty directories, so old checkouts may retain empty `.ai` folders. Remove only verified-empty leftovers, never unknown contents. See [Codex skills](https://learn.chatgpt.com/docs/build-skills) and [custom agents](https://learn.chatgpt.com/docs/agent-configuration/subagents).

Keep always-loaded guidance small: discover skill names/descriptions first, load only the selected workflow and relevant project notes, and inspect affected paths before widening the search. Do not copy the skill catalog, vault, or a full repository map into `AGENTS.md`. Checkpoints, cleanup state, and recovery archives stay local and ignored under `.codex/`; they are not permanent guidance. The exact-message shortcut `Sæt i gang` routes to the cleanup maintainer through `AGENTS.md`; a longer message containing those words does not trigger it.

Task size determines staffing, not a separate Agent OS. The primary agent handles small/standard work; large work separates coordination, implementation and read-only review. The detailed [lean orchestration procedure](.agents/skills/fitness-feature/SKILL.md#lean-orchestration) limits duplicated discovery, context transfer and verification. These are workflow safeguards, not enforced token limits or a measured cost/quality guarantee.

### Application layout

There is one hosted application and one startup project: `FitnessApp.Server`. The Blazor WebAssembly client and ASP.NET Core API run together from the existing server host:

```bash
dotnet run --project src/FitnessApp.Server --launch-profile https
```

Nutrition, strength training, running, calendar, and settings are features of this existing layered modular monolith; none adds a solution, application, executable, web host, or independent startup process.

```text
src/
  FitnessApp.Client          Blazor WebAssembly user interface
  FitnessApp.Server          ASP.NET Core host, API, and composition root
  FitnessApp.Contracts       Shared HTTP request and response contracts
  FitnessApp.Application     Application service contracts
  FitnessApp.Domain          Business rules and domain entities
  FitnessApp.Infrastructure  Identity and EF Core SQLite persistence
tests/
  FitnessApp.IntegrationTests
```

## Local secret configuration

The server requires `Authentication:Jwt:SigningKey`. Administrator bootstrap additionally requires `BootstrapAdmin:Email` and `BootstrapAdmin:Password`; `BootstrapAdmin:DisplayName` is optional. From the repository root, use this `zsh` snippet. It uses masked password input, generates a 384-bit signing key, JSON-encodes every value, and sends the JSON directly to the supported User Secrets standard-input command:

```zsh
read -r "FITNESSAPP_ADMIN_EMAIL?Administrator email: "
read -r "FITNESSAPP_ADMIN_DISPLAY_NAME?Administrator display name: "
read -r -s "FITNESSAPP_ADMIN_PASSWORD?Administrator password: "
printf '\n'

FITNESSAPP_JWT_KEY="$(openssl rand -base64 48 | tr -d '\n')"
export FITNESSAPP_ADMIN_EMAIL FITNESSAPP_ADMIN_DISPLAY_NAME
export FITNESSAPP_ADMIN_PASSWORD FITNESSAPP_JWT_KEY

python3 -c 'import json, os, sys; json.dump({
    "BootstrapAdmin:Email": os.environ["FITNESSAPP_ADMIN_EMAIL"],
    "BootstrapAdmin:DisplayName": os.environ["FITNESSAPP_ADMIN_DISPLAY_NAME"],
    "BootstrapAdmin:Password": os.environ["FITNESSAPP_ADMIN_PASSWORD"],
    "Authentication:Jwt:SigningKey": os.environ["FITNESSAPP_JWT_KEY"]
}, sys.stdout)' | dotnet user-secrets set --project src/FitnessApp.Server

unset FITNESSAPP_ADMIN_EMAIL FITNESSAPP_ADMIN_DISPLAY_NAME
unset FITNESSAPP_ADMIN_PASSWORD FITNESSAPP_JWT_KEY
```

The secret values are not placed in command arguments, shell history, or temporary files. The JSON import updates these keys while preserving unrelated existing User Secrets entries. User Secrets are a development convenience and are not an encrypted production vault. Never add secret values to settings files, client assets, documentation, images, or source control.

## Restore, migrate, and bootstrap

Restore packages and the repository-local EF Core tool:

```bash
dotnet tool restore
dotnet restore FitnessApp.slnx
```

Development startup applies pending migrations to the configured local SQLite database automatically. For an explicit migration step in another environment, run:

```bash
dotnet tool run dotnet-ef database update \
  --project src/FitnessApp.Infrastructure \
  --startup-project src/FitnessApp.Server
```

Create the initial approved administrator once:

```bash
dotnet run --project src/FitnessApp.Server -- bootstrap-admin
```

Bootstrap has no HTTP endpoint and accepts no password argument. It refuses to elevate an existing account. Once an approved administrator exists, rerunning the command reports that initialization is complete and makes no changes.

## Run locally over HTTPS

Trust the development certificate once if needed:

```bash
dotnet dev-certs https --trust
```

Start the hosted application (the `https` launch profile uses Development, so pending migrations are applied on startup):

```bash
dotnet run --project src/FitnessApp.Server --launch-profile https
```

Open <https://localhost:7192>. Only approved users can log in or use protected APIs.

## Registration and administrator approval

Visitors can choose **Opret bruger** on the login page. A valid submission creates one `Pending` Identity account with only the ordinary `User` role. It creates neither an access token nor a server session. Duplicate email submissions receive the same neutral accepted response as a new request and never overwrite the existing account. The endpoint has its own IP-partitioned rate limit.

After signing in, an administrator opens **Brugeranmodninger** from the protected home page. The bounded list contains only eligible pending registrations and supports `Pending → Approved` or `Pending → Rejected`. Rejection requires explicit confirmation. Decisions are atomic, so an already processed or concurrently decided request cannot be overwritten. Registration time, decision time, and deciding administrator ID are stored in UTC; the browser shows the registration time in the user's local timezone.

Approval permits a later login but does **not** prove ownership of the submitted email address. Registration and approval do not send email; the password-reset flow below sends email only for Approved accounts. Ordinary users do not see administrator navigation, and every administrator endpoint independently requires the live `Admin` role.

## Strength programs

An Approved user opens **Styrketræning** from the protected home page to create, view, edit, reorder, and delete personal programs. A program holds up to 12 ordered workouts, and every workout holds 1–50 ordered exercises. The Figma-aligned mobile flow includes today’s workout, weekly planning, start training, editable actual weight/sets/repetitions, completion, and the latest saved performance for every exercise. Each exercise stores a name, 0–1000 kg planned weight, 1–10 planned sets, 1–30 planned repetitions, and an optional note of up to 250 characters. Warm-up is represented as an ordinary exercise when wanted; there is no separate warm-up flag.

The server derives ownership from the validated session. Lists, reads, updates, and deletes are scoped to that user, including for administrators. Program and exercise changes commit atomically. A version token rejects stale updates or deletions made from another tab and lets the user retain the local draft before choosing whether to reload.

## Running

An Approved user opens **Løb** to see the active plan, this week's sessions, planned-session details, registered results, and the running calendar. **Resultathistorik** is the separate reopening route for all manual and archived-plan results; it is deliberately not a **Seneste løb** overview section, which is absent from the current design. Without a plan, the overview explains the empty state and opens the setup flow. The Figma Make version-47 source and published public preview were reviewed as visual/journey reference for the Danish multi-step setup, plan overview, session detail, active run, manual registration, result detail/correction, and calendar entry point. Figma review is not runtime or pixel-parity verification.

Setup records the runner's current level, the distance they can cover in 30 minutes, target distance and date, weekly frequency, and preferred weekdays. The server derives the owner from the approved session; administrators receive no private-data bypass. Scheduled dates are persisted as `DateOnly` and interpreted consistently in `Europe/Copenhagen`, using a server `TimeProvider` rather than the server's incidental local timezone; client date constraints and calendar display use the same Copenhagen-local day. Danish and invariant decimal input are accepted for kilometres. Server validation accepts a 0.5–15 km 30-minute ability, a 1–100 km target, a target date 7–365 days ahead, and exactly one to seven distinct weekdays matching the selected frequency.

Plan generation is local and deterministic. It schedules every selected weekday from the Copenhagen-local today through the target date, derives Easy, Tempo, Intervals, and Long Run sessions from the stated current ability, and checks the target against the capacity of the final scheduled session. Capacity grows by no more than 10% per completed week, so an infeasible target/date/frequency receives a clear validation response instead of a hard-coded prototype schedule. A new plan is refused while another is active; replacement requires explicit confirmation and the matching plan version, retires the former plan, and leaves its sessions and completed/manual history intact.

Planned sessions retain their type, target distance, pace range, and structure separately from actual results. A planned session can be completed with actual distance, duration, average heart rate, and an optional note, or marked completed without inventing measurements. Manual registration requires a date through Copenhagen-local today plus actual distance and duration; its average heart rate and note are optional. Corrections retain distance and duration for a manual result, while a planned completion may remain measurement-free. Pace is calculated only when a non-zero actual distance and duration exist. Completion uses a unique owner-scoped idempotency value, and saved results use a version value so a stale editor can retain its draft and reload deliberately.

Starting a non-future planned run records its server timestamp and is safe to repeat; a plan permits only one active, uncompleted run at a time, and a conflicting start takes the runner to that existing run. Cancellation clears only an active, uncompleted start. The active screen derives elapsed time from that recorded source and the current clock; it does not manufacture live distance, pace, heart rate, GPS, maps, background tracking, or sensor data. Recoverable load/save failures preserve entered values and offer Danish retry or cancel paths; a stale plan refreshes its version while preserving the setup draft, and destructive/replacement choices require confirmation. Future calendar days do not imply that a manual result can be registered early.

Running uses the shared EF migration history. Development startup applies pending migrations; other environments use the explicit procedure under [Restore, migrate, and bootstrap](#restore-migrate-and-bootstrap). Applied migration state belongs to each environment and is not recorded in this README.

## Password reset and email delivery

The public Danish flow starts at **Glemt adgangskode?** on the login page. Every valid request receives the same response, whether the account is unknown, Pending, Rejected, or Approved. Only Approved accounts are eligible for delivery and eligibility is checked again when a password is changed. Requests have an IP rate limit and an atomic five-minute per-account cooldown. Email work is placed on a bounded in-memory queue so SMTP latency does not disclose whether an account exists.

Reset links use ASP.NET Core Identity's dedicated password-reset token provider with a one-hour lifetime. The origin comes only from the validated `PublicApp:BaseUrl`; it is never derived from a request Host header. Tokens are Base64url-encoded. Reset pages and APIs use `no-store`, and the reset document applies `Referrer-Policy: no-referrer`; the client has no external assets or analytics. A successful reset changes the password and revokes all existing application sessions in one SQLite transaction, preserves approval and roles, and does not sign the user in.

The queue holds at most 32 messages. Delivery is tried twice with a two-second delay. Failures remain neutral to the visitor and produce structured events containing an internal message ID and error type, never the recipient, content, credentials, token, or URL. A full queue drops the message and releases its cooldown reservation so a later request can retry. Because the queue is intentionally in memory, pending mail is lost if the process stops; this slice does not add a broker or durable outbox.

Production SMTP uses MailKit, Brevo's relay on port 587, required STARTTLS, normal platform certificate validation, cancellable async calls, and a 15-second timeout. There is no certificate bypass, SMTP protocol log, or fallback transport. SMTP mode fails startup if its configuration is incomplete. `Smtp:FromEmail` and `Smtp:FromName` are configurable so an authenticated domain sender can replace them later without code changes. The owner confirmed successful local Brevo delivery before the strength-program work; automated verification does not send real email.

All previously shared Brevo keys must be revoked. Generate a fresh SMTP key and enter it locally without pasting it into chat. Development defaults to a private pickup directory and needs no SMTP credential. To opt into a separate real-email smoke test, this `zsh` snippet reads the fresh key without echo, keeps it out of command arguments and temporary files, and sends JSON through standard input:

```zsh
read -r -s "FITNESSAPP_SMTP_PASSWORD?Fresh Brevo SMTP key: "
printf '\n'
export FITNESSAPP_SMTP_PASSWORD

python3 -c 'import json, os, sys; json.dump({
    "Email:Transport": "Smtp",
    "Smtp:Password": os.environ["FITNESSAPP_SMTP_PASSWORD"]
}, sys.stdout)' | dotnet user-secrets set --project src/FitnessApp.Server

unset FITNESSAPP_SMTP_PASSWORD
```

User Secrets live outside the repository, but they are not encrypted and are only a development convenience. After the test, remove the transport override to return to pickup mode:

```bash
dotnet user-secrets remove "Email:Transport" --project src/FitnessApp.Server
dotnet user-secrets remove "Smtp:Password" --project src/FitnessApp.Server
```

Do not put SMTP passwords, JWT signing keys, or future credential-bearing connection strings in Git, client configuration, logs, screenshots, or documentation. Deployment must supply runtime secrets through its environment or restricted secret files. GitHub Actions Secrets are workflow inputs and do not automatically become application runtime configuration.

The equivalent environment-variable names use double underscores:

```text
Smtp__Host
Smtp__Port
Smtp__Username
Smtp__Password
Smtp__FromEmail
Smtp__FromName
PublicApp__BaseUrl
ConnectionStrings__DefaultConnection
DataProtection__KeyRingPath
Email__Transport
```

`PublicApp:BaseUrl` has no production default and must be set to the application's real public HTTPS origin. Development uses the actual launch-profile origin, `https://localhost:7192`. Only a loopback HTTP URL is permitted in Development; other environments require HTTPS.

Development and automated tests use the explicit `Pickup` transport. It writes private `.eml` files under `src/FitnessApp.Server/App_Data/email-pickup`, outside `wwwroot`, with a mode-0700 directory and mode-0600 files on Unix. The directory is ignored by Git and has no HTTP endpoint. These files contain live local reset links: inspect them only for local verification, do not attach or commit them, and delete them when finished.

## Local Development SMTP certificate revocation exception

Some macOS/.NET connections to Brevo fail with `SslHandshakeException` and `RevocationStatusUnknown` even when the certificate chain and hostname are otherwise valid. An explicit local-only opt-in is available:

```bash
dotnet user-secrets set "Smtp:AllowLocalDevelopmentRevocationBypass" "true" --project src/FitnessApp.Server
dotnet run --project src/FitnessApp.Server --launch-profile https
```

Stop the existing server before restarting it. This flag does not select SMTP or change credentials; configure `Email:Transport=Smtp` and a fresh SMTP key as described above. It defaults to false and is never enabled in tracked settings. The environment-variable equivalent is `Smtp__AllowLocalDevelopmentRevocationBypass`.

Startup rejects the opt-in unless the environment is `Development` and `PublicApp:BaseUrl` is loopback. At delivery time, the sender also requires a nonempty set of exclusively loopback server listener addresses. Production, Staging, Testing, public origins, wildcard/network listeners, and unknown listener addresses cannot use the exception. Do not expose this local instance through a reverse proxy or tunnel; loopback checks cannot detect those.

The exception only disables certificate revocation checking. Required STARTTLS, certificate expiry, hostname, and trust-chain validation remain enabled, but a subsequently revoked otherwise valid certificate could be accepted. Event 1312 explicitly reports each use without any credentials or email content. Remove the opt-in and restart to restore revocation checking:

```bash
dotnet user-secrets remove "Smtp:AllowLocalDevelopmentRevocationBypass" --project src/FitnessApp.Server
```

## Persistent security storage

The non-secret development connection string remains `ConnectionStrings:DefaultConnection=Data Source=App_Data/fitnessapp.db`. The resolved SQLite file is under the server content root, outside `wwwroot`; database files, journals, and backups are ignored. Restrict the directory to the application account and protect backups. Any future connection string containing credentials must be supplied as a secret.

Identity reset tokens depend on ASP.NET Core Data Protection. The application sets the explicit identity `FitnessApp` and persists its key ring at `DataProtection:KeyRingPath`, defaulting locally to `src/FitnessApp.Server/App_Data/data-protection-keys`. The directory is outside `wwwroot`, ignored by Git, and set to mode 0700 on Unix. Normal restarts retain valid links as long as the same application identity and key ring are used.

For a future runtime, configure an absolute persistent path readable and writable only by the application account. Back it up with the database, do not place it in a publicly served or repository directory, and protect the filesystem or volume with encryption at rest. Explicit filesystem persistence disables Data Protection's automatic at-rest key encryption; this implementation relies on restricted filesystem access and the host's encrypted storage rather than inventing a deployment-specific certificate or vault.

## Manual email smoke test

Real delivery is deliberately separate from automated verification. After rotating the exposed keys and using the masked setup above:

1. Confirm `PublicApp:BaseUrl` is the exact HTTPS origin being tested.
2. Start the app, request a reset for an Approved test account, and confirm one message arrives from the configured verified sender.
3. Open the link, set a new policy-compliant password, and verify the old password and all prior sessions fail while the new password works.
4. Record the result without copying credentials, tokens, complete reset URLs, or personal email content.

The owner completed this delivery check successfully before the strength-program work. This repository records that as owner-confirmed operational evidence rather than an independently repeated automated test.

## Authentication behavior

The server issues signed HS256 JWT access tokens with a 15-minute lifetime and explicit 30-second clock skew. JwtBearer validates the signature, algorithm, issuer, audience, and expiry. Every protected request also checks the persisted session and current account approval state. Logout revokes that session immediately. Login uses Identity password validation, lockout, and an IP-partitioned rate limit; failures deliberately return the same Danish message.

The client attaches the bearer token only to same-origin `/api/` requests and reacts automatically only to 401 responses from that same API. Recoverable home-page failures offer a retry. Logout always clears the local memory-only session; if server revocation cannot be confirmed, the login page says so rather than claiming a complete server logout. Memory-only storage reduces durable token exposure but means a reload, closed tab, or expired token requires login again. Refresh tokens and “remember me” are intentionally deferred. Compared with cookie authentication, bearer JWTs avoid cookie/CSRF semantics and fit an API client, but they require careful attachment, XSS protection, short lifetimes, and explicit server-side revocation; the persisted session supplies that revocation for this slice.

## Development workflow

New work uses `feature/<description>`, urgent fixes to main use `hotfix/<description>`, and other unexpected behavior uses `bugfix/<description>`. The single detailed policy is [Branch lifecycle](.agents/skills/fitness-pr/SKILL.md#branch-lifecycle), including maintenance naming and safe retirement of existing legacy names. After a task's PR merges into main, Codex deletes its completed branch locally and on origin when it observes the merge and verifies safety. No watcher is installed: if the task ended before merge, cleanup remains pending until the next task. Main, unfinished work, active PRs, and uncertain history are preserved and reported rather than force-deleted.

Use the [native AI workspace layout](#ai-workspace-layout) and shared local/CI verification entrypoint `./scripts/verify.sh`. The workflow is documented in the private vault note `30 Udvikling/Repository-workflow.md`. Project hooks require explicit review and trust through `/hooks`; the repository does not bypass that protection.

The [Project knowledge loop](AGENTS.md#project-knowledge-loop) governs relevant Obsidian updates during authorized change tasks, including the no-vault fallback and PR handoff. It is not a background synchronizer. A GitHub reviewer does not automatically receive a local vault; `.github/PULL_REQUEST_TEMPLATE.md` carries only the minimum non-sensitive review context and any pending knowledge reconciliation.

## Verification

```bash
./scripts/verify.sh verify
./scripts/verify.sh audit
```

The integration suite uses isolated real SQLite databases, not EF InMemory. Run the commands above for current verification evidence.

## Future operations

Pull requests targeting `main` run `.github/workflows/pr-verification.yml`, which calls the same shared verification script with read-only repository permissions and no production secrets or deployment. When deployment work begins, CI/CD secrets belong in GitHub Actions Secrets and the Raspberry Pi requires separate runtime secret provisioning. Evaluate a genuinely no-license-cost vault together with its operational burden before adoption; do not assume Azure Key Vault or another hosted service remains permanently free. A private Grafana/Loki dashboard with bounded retention is a future logging candidate. No deployment, vault, container, Pi, cloud, or log-shipping infrastructure is included in this slice.
