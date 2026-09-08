# FitnessApp

FitnessApp is a private, mobile-first fitness application in an intentionally public source repository. Protected functionality requires an approved ASP.NET Core Identity account. The current vertical slices provide SQLite persistence, a one-time local administrator bootstrap, public registration with administrator approval, signed JWT login, live session validation, a protected Danish home page, and logout.

Product scope and the distinction between implemented and deferred work are documented in [docs/project.md](docs/project.md). Verification evidence and blockers are tracked in [docs/progress.md](docs/progress.md).

The implemented authentication surfaces follow the current Figma Make direction: charcoal/gray surfaces, navy primary actions, and light-blue focus and accent states. Registration, confirmation, and administrator review extend that system because the current Make source contains a login screen but no equivalent frames for those flows.

## Prerequisites

- .NET SDK 10.0.103 or a later compatible .NET 10 feature-band SDK, as controlled by `global.json`
- OpenSSL for generating local signing material
- Python 3 for safely JSON-encoding the one-shot User Secrets input below
- A trusted ASP.NET Core development certificate for browser-based HTTPS testing

No Node.js runtime, container engine, or cloud account is required.

## Repository structure

The solution file is `FitnessApp.slnx`.

```text
src/
  FitnessApp.Client          Blazor WebAssembly user interface
  FitnessApp.Server          ASP.NET Core host, API, and composition root
  FitnessApp.Contracts       Shared HTTP request and response contracts
  FitnessApp.Application     Authentication application contracts
  FitnessApp.Domain          Business rules and account approval state
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

Apply the tracked migration to the configured local SQLite database:

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

Start the hosted application:

```bash
dotnet run --project src/FitnessApp.Server --launch-profile https
```

Open <https://localhost:7192>. Only approved users can log in or use protected APIs.

## Registration and administrator approval

Visitors can choose **Opret bruger** on the login page. A valid submission creates one `Pending` Identity account with only the ordinary `User` role. It creates neither an access token nor a server session. Duplicate email submissions receive the same neutral accepted response as a new request and never overwrite the existing account. The endpoint has its own IP-partitioned rate limit.

After signing in, an administrator opens **Brugeranmodninger** from the protected home page. The bounded list contains only eligible pending registrations and supports `Pending → Approved` or `Pending → Rejected`. Rejection requires explicit confirmation. Decisions are atomic, so an already processed or concurrently decided request cannot be overwritten. Registration time, decision time, and deciding administrator ID are stored in UTC; the browser shows the registration time in the user's local timezone.

Approval permits a later login but does **not** prove ownership of the submitted email address. The application sends no email in this slice. Ordinary users do not see administrator navigation, and every administrator endpoint independently requires the live `Admin` role.

## Authentication behavior and current limitation

The server issues signed HS256 JWT access tokens with a 15-minute lifetime and explicit 30-second clock skew. JwtBearer validates the signature, algorithm, issuer, audience, and expiry. Every protected request also checks the persisted session and current account approval state. Logout revokes that session immediately. Login uses Identity password validation, lockout, and an IP-partitioned rate limit; failures deliberately return the same Danish message.

The client attaches the bearer token only to same-origin `/api/` requests and reacts automatically only to 401 responses from that same API. Recoverable home-page failures offer a retry. Logout always clears the local memory-only session; if server revocation cannot be confirmed, the login page says so rather than claiming a complete server logout. Memory-only storage reduces durable token exposure but means a reload, closed tab, or expired token requires login again. Refresh tokens and “remember me” are intentionally deferred. Compared with cookie authentication, bearer JWTs avoid cookie/CSRF semantics and fit an API client, but they require careful attachment, XSS protection, short lifetimes, and explicit server-side revocation; the persisted session supplies that revocation for this slice.

## Development workflow

Repository-native Codex skills, read-only reviewer agents, the resume checkpoint/hook, and the shared local/CI verification entrypoint are documented in [docs/development-workflow.md](docs/development-workflow.md). Project hooks require explicit review and trust through `/hooks`; the repository does not bypass that protection.

## Verification

```bash
./scripts/verify.sh verify
./scripts/verify.sh audit
```

The integration suite uses isolated real SQLite databases, not EF InMemory. The latest verification passed 37 of 37 tests. A fresh direct/transitive NuGet advisory retrieval completed against `https://api.nuget.org/v3/index.json` and reported no known vulnerable packages in all seven projects. See [docs/progress.md](docs/progress.md) for exact build, browser, migration, bootstrap, design, and environment results.

## Future operations

Pull requests targeting `main` run `.github/workflows/pr-verification.yml`, which calls the same shared verification script with read-only repository permissions and no production secrets or deployment. A narrow temporary trigger also supports the tooling pull request while it is stacked on the open registration branch. When deployment work begins, CI/CD secrets belong in GitHub Actions Secrets and the Raspberry Pi requires separate runtime secret provisioning. Evaluate a genuinely no-license-cost vault together with its operational burden before adoption; do not assume Azure Key Vault or another hosted service remains permanently free. A private Grafana/Loki dashboard with bounded retention is a future logging candidate. No deployment, vault, container, Pi, cloud, or log-shipping infrastructure is included in this slice.
