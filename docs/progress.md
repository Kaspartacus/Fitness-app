# Progress

Last updated: 2026-09-08

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
- Added `.github/workflows/pr-verification.yml` for pull requests to `main`: tool/package restore, serial build, and tests with read-only repository permissions, no production secrets, and no deployment.

## Design evidence

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

- Approval does not verify email ownership, and the application sends no email. Password reset is not implemented.
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
