# Progress

Last updated: 2026-09-10

## Strength-training Figma flow

- Strength training now matches the inspected Make version-44 information architecture: program → ordered workouts → ordered exercises. The Danish mobile UI includes today’s scheduled workout, program cards, workout detail cards, **Start træning**, **Planlæg**, a seven-day plan, and a full active-workout screen.
- The active-workout screen records actual weight, sets, repetitions and completion per exercise. Completed values are persisted separately from the plan and the next active workout shows the latest previous performance as **Sidst**. Plans can have up to 12 workouts; a workout has 1–50 exercises. Program creation deliberately begins without workouts so the user can name a program first.
- All strength endpoints are still derived from the approved JWT subject and scoped to that user. The owned aggregate uses an optimistic version token for program and schedule writes. Completed history is user-owned and remains independent when program definitions later change.
- `20260910191738_AddStrengthTrainingFlow` reconstructs `ProgramExercises` for SQLite, preserves every existing direct program/exercise list as a `Træning 1` workout, and adds schedules plus completed-workout tables. The configured local SQLite database was migrated successfully; its existing program and three exercises are preserved and `PRAGMA foreign_key_check` is clean. A local backup was saved outside the repository before applying it.
- Figma was directly inspected for the relevant strength screens. The implementation uses its near-black canvas, charcoal cards, muted navy controls, light-blue accents, compact mobile spacing, top-left back control, mobile-only bottom navigation, and the active-workout stepper layout. Warm-up remains an ordinary exercise; no warm-up boolean exists.
- The UI now uses one centered, accessible modal for exercise add/edit, exercise/workout/program deletion, weekly selection, workout completion, and administrator rejection. It traps focus, restores focus, supports Escape and safe backdrop cancellation, locks background scrolling, and scrolls internally for tall content. Exercise editing is no longer a permanent page form; compact cards open the same modal with current values. Save failures retain program and workout drafts; internal navigation, workout cancellation, and exercise-modal cancellation ask before discarding unsaved work; and a dialog cannot be dismissed during a destructive request.
- The owner requested no app run, browser verification, tests, audit, or shared verification script for this refinement. The one permitted `dotnet build FitnessApp.slnx --no-restore` completed with no errors; the only output was the environment's known `NU1900` advisory-DNS warning.
## Local Development SMTP follow-up

- The owner authorized a local Development-only exception after reproducing `SslHandshakeException` / `RevocationStatusUnknown` on macOS 26.5 with .NET 10.0.3 and MailKit 4.17.0. Credential-free diagnostics confirmed that STARTTLS works when only revocation checking is disabled; hostname/trust validation remained enabled. No actual delivery is claimed from this probe.
- Added `Smtp:AllowLocalDevelopmentRevocationBypass`, default false, with startup checks for Development and a loopback public origin. The sender additionally checks actual listener addresses before connecting and refuses nonlocal, wildcard, mixed, missing, or unknown listeners. Event 1312 records explicit use without credentials or content. Reverse proxies/tunnels must not expose this local instance.
- Activated this non-secret flag in the owner's local User Secrets without reading or changing SMTP credentials. The existing server must be restarted to load the change.
- Added 12 tests for environment/origin/listener guards, startup rejection, and actual STARTTLS rejection of an untrusted certificate with the exception both enabled and disabled. Full shared verification passed: **72 tests, 0 failed/skipped**, build **0 warnings/errors**, and whitespace checks passed. Pre-existing malformed build-output directories were temporarily set aside during the scan and restored unchanged.
- Updated server started successfully on separate local HTTPS port 7193 with pickup transport; Blazor login loaded with no browser warnings/errors. Temporary server and resources were cleaned up. The owner's existing server was left running.
- Independent read-only correctness/security review found no actionable defects. No packages or UI changed. Changes remain local on `fix/local-development-smtp`; no new commit, push, PR, merge, or deployment was requested.

## Completed authentication foundation

- The .NET 10 hosted Blazor WebAssembly solution uses separate Client, Server, Contracts, Application, Domain, and Infrastructure projects.
- ASP.NET Core Identity and EF Core persist users, `Admin`/`User` roles, lockout state, approval state, and server-side sessions in SQLite.
- The explicit `bootstrap-admin` command creates the first Approved administrator once, refuses to elevate an existing account, and is idempotent.
- HS256 JWT login validates signature, algorithm, issuer, audience, expiry, persisted session, live approval, and live role membership. Logout immediately revokes the current session.
- Client tokens remain in memory and are attached only to same-origin `/api/` calls. Reloading or closing the tab requires login again.
- Existing generic login failures, lockout, rate limiting, protected-home retry, same-origin handling, and failure-aware logout behavior remain intact.

## Completed registration and administrator-approval slice

- Added a Danish registration flow from the login page with display name, email, password, and password confirmation. DataAnnotations run in the client and server, and Identity enforces the persisted password policy and normalized uniqueness.
- Public input maps through an explicit request contract and cannot set roles, approval state, email confirmation, or decision metadata.
- A valid request creates exactly one `Pending` account with only the `User` role, a UTC registration timestamp, no JWT, and no server session. The button and handler both guard duplicate submission.
- Existing-email and concurrent duplicate requests receive the same neutral HTTP 202 response without changing the existing account. Invalid form input still returns field-level validation. Registration has its own IP-partitioned fixed-window rate limit.
- Added an administrator-only, bounded pending-registration endpoint and Danish review page. Ordinary users do not see its home-page navigation; anonymous requests receive 401 and authenticated ordinary users receive 403 from the server.
- Pending registrations can transition atomically only to `Approved` or `Rejected`. A repeated or concurrent stale decision returns HTTP 409 and cannot overwrite the winner. The decision stores UTC time and deciding administrator ID while preserving the `User` role.
- The UI covers loading, empty, success, forbidden/unavailable errors, retry, disabled in-flight actions, local-time display, and explicit confirmation before rejection. Authenticated users without the required role receive a dedicated no-access view for client-side protected navigation.
- Added `20260907185305_AddRegistrationApprovalMetadata`, with nullable `RegisteredAt`, `DecidedAt`, and `DecidedByUserId` fields plus a pending-list index. Existing users retain null metadata.
- Added structured events 1100-1101 for registration and 1200-1202 for decisions. Logs use internal IDs/trace IDs and omit passwords, tokens, request bodies, email addresses, and display names.

## Password-reset and email slice

- Added public Danish forgot-password, neutral confirmation, new-password, success, and invalid/expired-link states, plus the login-page entry point. Forms include bounded validation, password confirmation and visible requirements, autocomplete metadata, in-flight duplicate prevention, accessible status/errors, and retryable network failures.
- Valid forgot-password requests always return the same accepted message. Only Approved accounts queue mail. A fixed-window IP rate limit, an atomic persisted five-minute account cooldown, and a minimum response duration reduce abuse and account-disclosure signals.
- Reset links use Identity's dedicated Data Protection token provider with an explicit one-hour lifetime and Base64url encoding. Their origin comes only from validated `PublicApp:BaseUrl`; production has no default and requires HTTPS. Reset routes and APIs use no-store behavior and the document uses a no-referrer policy.
- Reset eligibility is rechecked immediately before the password change. Password update and revocation of every existing database session commit in one transaction. Reset changes Identity's security stamp, and sessions record that stamp so a login racing the reset cannot leave an old-stamp JWT valid. Approval and role assignments are untouched, and no automatic login occurs.
- Added a small application email boundary, a bounded in-memory queue, and structured delivery events without recipients or content. The worker makes at most two cancellable attempts, respects shutdown, and loses pending messages on process restart by design. A full queue releases its cooldown reservation for a later request.
- Added MailKit 4.17.0 SMTP delivery with required STARTTLS on port 587, normal certificate validation, a bounded timeout, and no protocol logging. SMTP mode validates its full configuration and cannot silently fall back.
- Development and Testing use an explicit pickup transport that writes mode-0600 `.eml` files into a mode-0700 ignored directory outside `wwwroot`. It has no public endpoint and ordinary logs contain neither reset URLs nor message content.
- Data Protection now has the stable application identity `FitnessApp` and an explicit persistent key-ring path outside Git and `wwwroot`. The local directory is restricted to the current Unix user; future hosts must also supply encrypted persistent storage or another explicit at-rest protection appropriate to that environment.
- Added `20260909044755_AddPasswordResetCooldown`, which adds nullable cooldown metadata and nullable per-session security-stamp metadata. Existing accounts, password hashes, roles, approval metadata, and sessions remain representable during upgrade.
- README configuration now covers credential rotation, masked User Secrets setup, environment-variable names, trusted base URL, SQLite and key-ring protection, test transport, retry/loss semantics, and the separate real-delivery smoke check. Previously shared Brevo keys remain prohibited; actual Brevo delivery is unverified.

## Current password-reset verification

- Individual Client, Infrastructure, Server, and integration-test project builds passed with zero warnings and errors after one header API correction.
- The first sandboxed EF invocation reproduced the documented external task-host stall. Only its confirmed tool session was stopped; its two malformed untracked output directories were removed. Migration generation then succeeded with the documented out-of-sandbox workaround.
- **25 focused tests passed, 0 failed, 0 skipped**: 21 password-reset endpoint/transport/configuration cases, two client response-mapping cases, and two empty/upgrade migration cases. Coverage includes neutral responses, Approved-only delivery, eligibility changes, cooldown, IP rate limiting, queue saturation, retry bounds, SMTP failure neutrality, trusted origin, URL-safe tokens, success, old/new passwords, replay, malformed/tampered/expired tokens, concurrent use, password policy, all-session and racing-session rejection, approval/role preservation, GET non-mutation, log redaction, invalid configuration, and same-key-ring restart behavior.
- Final `./scripts/verify.sh verify`: **60 passed, 0 failed, 0 skipped**, build **0 warnings, 0 errors**, and tracked/untracked/committed whitespace checks passed. `./scripts/verify.sh audit` retrieved current advisory data and found no known vulnerable direct or transitive packages across all seven projects. EF `has-pending-model-changes --no-build` confirmed the model matches the migration.
- HTTPS browser verification at `https://localhost:7192` used an isolated synthetic SQLite database and explicit private pickup transport. Verified login entry point, Approved-account request with one captured message (content not exposed), neutral confirmation, disabled in-flight submit, malformed-email invalid-link recovery, reset form and required-field validation, and stopped-server network failure followed by successful retry after restart.
- Inspected desktop, 390 px, and 360 px layouts using the existing design tokens as consistent extensions, not new Figma matches. Checked documents had no horizontal overflow; 360 px reset inputs measured 328 × 52 px. No console warnings/errors appeared during normal flow; the intentional stopped-server request produced the expected network failure.
- The browser tool requires human handoff before entering/submitting a new password, so the captured-link → password submission → new-password login browser sequence remains a manual check. The corresponding server sequence, old-password rejection, session revocation, replay, and restart behavior passed integration tests. Real Brevo delivery remains unverified.
- Separate read-only correctness and security reviews identified malformed query fields silently blocking client validation and a redaction test that suppressed successful Information events. Both were fixed and re-reviewed with no remaining concrete defects. The UI now validates link fields before rendering and resets terminal state for changed links; redaction coverage requires all successful-flow events and requests the reset URL.
- The EF command regenerated the known malformed `bin\Debug` build directories even with `--no-build`; those generated artifacts were removed from this feature worktree before the final file review. The original checkout remains untouched.
- Added `.github/workflows/pr-verification.yml` for pull requests to `main`: tool/package restore, serial build, and tests with read-only repository permissions, no production secrets, and no deployment.

## Design evidence

## Strength-program architecture confirmation

- The strength-program vertical slice remains inside the sole `FitnessApp.slnx` solution and the existing Client, Server, Application, Domain, Infrastructure, and Contracts projects.
- `FitnessApp.Server` remains the sole hosted startup project. `dotnet run --project src/FitnessApp.Server` starts the combined Blazor WebAssembly client and ASP.NET Core API.
- No separate strength-training solution, application, executable, web host, or startup process was created. The feature is organized in existing-project `Strength` folders, with its EF migration retained in Infrastructure persistence.

- The current Make file `dFJcR42XWiqVhBtA1bfyOS`, node `0:1`, was inspected through the Figma design-context integration and browser. It reported version 41 and returned the real source inventory, including `App.tsx`, `index.css`, `LoginScreen.tsx`, shared components, refinement notes, and screen files. The Figma file was not modified.
- Visible version-41 source notes establish the current direction: filled navy primary actions, navy/light-blue active states, readable gray-blue inactive states, and removal of the former purple hard-coded surfaces and tints. The Blazor stylesheet now centralizes a charcoal/gray/navy/light-blue token set and uses it across login, registration, confirmation, protected home, and administrator review.
- The exact pulse-mark SVG remains reused in the Blazor login, registration, and protected-home UI.
- The source inventory contains a login screen but no registration, confirmation, or administrator-review screens. Those screens implement the requested real states as a consistent extension and are not described as pixel-perfect matches to absent frames.
- The current Make live preview displayed `Couldn't load Make` during this run. This prevents a same-frame pixel comparison, but not inspection of the current source inventory or visible version-41 design notes.

## Verification results

- `dotnet tool restore`: passed (`dotnet-ef` 10.0.3).
- Clean serial `dotnet restore FitnessApp.slnx`: passed for all seven projects.
- Final serial `dotnet build FitnessApp.slnx --no-restore`: passed with 0 errors and three NU1900 advisory-DNS warnings inherited from restore. A fresh dedicated advisory command completed successfully as described below.
- `dotnet test FitnessApp.slnx --no-build --no-restore`: **37 passed, 0 failed, 0 skipped**.
- Pull request #1's `build-and-test` GitHub Actions job passed in 58 seconds on the initial feature commit.
- The 12 new registration/administration tests passed, including concurrent duplicate registration, public privilege-input rejection, rate limiting, 401/403 authorization, bounded pending selection, approval/rejection, audit metadata, repeated decisions, and simultaneous opposing decisions.
- Two isolated migration tests passed: latest migration on an empty SQLite database, and upgrade from `20260907121032_InitialIdentity` while preserving an existing account, password hash, approval status, and role assignment with new metadata left null.
- `dotnet-ef migrations has-pending-model-changes --no-build` passed outside the sandbox and confirmed that the EF model matches the tracked migration snapshot.
- A separate fresh-database bootstrap run applied both migrations and created exactly one Approved administrator with one Admin role. A second run made no changes and preserved its password hash and display name. The temporary database was removed.
- A fresh `dotnet list FitnessApp.slnx package --vulnerable --include-transitive --no-restore` retrieval used `https://api.nuget.org/v3/index.json`, exited 0 without NU1900, and reported no known vulnerable direct or transitive packages in all seven projects.
- HTTPS browser verification at `https://localhost:7192` used an isolated SQLite database and synthetic test accounts. Verified: registration confirmation; Pending login denial; administrator login/navigation/list; approval; administrator logout; approved user login with no administrator navigation; rejection confirmation and rejection; Rejected login denial; empty list; and a stopped-server network error followed by successful retry after restart.
- The current local app was visually and interactively inspected over HTTPS at desktop, 390 px, and 360 px widths. The desktop login retains the 382 px composition; the 360 px registration controls are 328 px wide and 52 px high; all checked documents matched their viewport width with no horizontal overflow. Long display names and email addresses wrap within home and administrator cards.
- The 360 px administrator check exposed oversized stacked action buttons caused by an `8rem` flex basis becoming vertical at the narrow breakpoint. The basis was corrected to content sizing. Reverification measured 44 px-high full-width stacked actions at 360 px and 44 px-high side-by-side actions at 390 px.
- Current HTTPS browser flow verified registration confirmation, Pending login denial, administrator login and protected navigation, approval, administrator logout, approved ordinary-user login without administrator navigation, and ordinary-user logout. Interactive focus remains visibly outlined. The application tab produced no browser-console warnings or errors during the flow.
- The local HTTPS verification server was stopped, its browser tabs were closed, and the database containing only synthetic test data was removed.

## Environment-specific findings

- Sandboxed WebAssembly builds can stall in the external `ComputeWasmBuildAssets` MSBuild task host. Serial builds outside that process restriction succeed without source changes using `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`, `MSBUILDDISABLENODEREUSE=1`, `-m:1`, and `-p:BuildInParallel=false`.
- EF migration generation inside the same sandbox showed the related task-host stall. Running the repository-local EF tool outside that restriction generated the migration normally. Two malformed untracked `bin\Debug` output directories created during the interrupted attempt were inspected and removed.
- NuGet advisory DNS resolution is intermittent in this environment: the final restore/build surfaced three NU1900 warnings, while the fresh dedicated audit completed normally moments earlier and retrieved current advisory data for every project.
- Figma Make's current live preview failed to load in the browser even though the design-context integration returned the source inventory and the page exposed version-41 refinement notes. This is a Figma preview/service issue, not an application runtime failure.

## Deliberate limitations and review status

- Approval still does not verify email ownership. Password reset now sends only to Approved accounts, but real Brevo delivery remains unverified until the owner rotates exposed keys and performs the documented smoke check.
- Access tokens remain memory-only; reload, tab closure, or expiry requires login. Refresh tokens and remember-me are not implemented.
- No deployment, runtime secret vault, log shipping, Raspberry Pi/Docker setup, or fitness module was added.
- Pull request #1 was merged into `main` as `5771df9df5f9f93716482899e0232f3f7ceb70b4`. Registration and administrator frames were absent from the inspected Figma source, so exact pixel parity for those absent screens was never claimed.

## Codex workflow tooling

- The recovery branch adds four non-overlapping project skills, two bounded read-only reviewer agents, one shared local/CI verification script, a safe opt-in resume hook, a concise checkpoint convention, and focused workflow/design documentation. It does not change application behavior.
- Pull request #2 was merged as `3586df3d357683593355db4161aab820b862b257` into its old feature-branch base after pull request #1 had already merged, so its tooling did not reach `main`. The recovery branch is based on current `origin/main` and integrates that existing merge history without changing files under `src/` or `tests/`.
- The temporary stacked-PR workflow trigger was removed. The shared script now changes to the repository root, works when invoked through an absolute path containing spaces from an unrelated directory, and validates structured audit output against the actual project identities in `FitnessApp.slnx` instead of a fixed project count.
- The read-only `reviewer` and `security-reviewer` reports identified concrete audit cleanup, diagnostic-handling, and path-disclosure gaps; all were corrected. Their question about absent framework arrays in a clean audit was resolved against the pinned SDK's observed structured output, which omits those arrays when there are no findings.
- Shell syntax, Python compilation, JSON, TOML, YAML front matter, session-hook execution, audit-parser self-tests, failure-path cleanup, and diff checks passed. The shared verification entrypoint passed both from the repository root and from `/private/tmp` via the absolute script path: build **0 warnings, 0 errors**; tests **37 passed, 0 failed, 0 skipped**.
- A current structured dependency audit matched all seven projects in `FitnessApp.slnx` and reported no known direct or transitive vulnerabilities. No database, credential, generated output, malformed build directory, or product-code change is included in the recovery diff.
- Recovery merge commit `42980a6e79785416ed952226ef83fc598246a677` is pushed on `fix/integrate-codex-workflow`, and pull request #3 targets `main`: `https://github.com/Kaspartacus/Fitness-app/pull/3`. It remains open for owner review; no merge or deployment is authorized.
