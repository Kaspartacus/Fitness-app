# Progress

Last updated: 2026-09-07

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

- The required direct design-context attempt for Make file `dFJcR42XWiqVhBtA1bfyOS`, node `0:1`, returned only a resource-listing instruction and no readable source or screenshot. It was not retried, and the Figma file was not modified.
- The current published prototype at `https://trance-vine-53032594.figma.site/` was inspected in the browser at desktop and phone widths. Verified login tokens include `#0f0f13` background, `#252533` fields, `#4db89e` accent, off-white/muted text, Inter/system typography, 52 px pill controls, 382 px desktop form width, 24 px phone margins, and no phone overflow in the prototype.
- The exact pulse-mark SVG was read from the published page and reused in the Blazor login, registration, and protected-home UI. The temporary letter placeholder was removed.
- The published prototype has no registration, confirmation, or administrator-review screens. Those screens implement the requested real states as a consistent visual extension; they have been functionally inspected but cannot be described as pixel-perfect matches to absent Figma frames.

## Verification results

- `dotnet tool restore`: passed (`dotnet-ef` 10.0.3).
- Clean serial `dotnet restore FitnessApp.slnx`: passed for all seven projects.
- Final serial `dotnet build FitnessApp.slnx --no-restore`: passed with 0 errors. Three NU1900 warnings remained because advisory metadata DNS lookup for `api.nuget.org` failed in Client, Infrastructure, and Server.
- `dotnet test FitnessApp.slnx --no-build --no-restore`: **37 passed, 0 failed, 0 skipped**.
- Pull request #1's `build-and-test` GitHub Actions job passed in 58 seconds on the initial feature commit.
- The 12 new registration/administration tests passed, including concurrent duplicate registration, public privilege-input rejection, rate limiting, 401/403 authorization, bounded pending selection, approval/rejection, audit metadata, repeated decisions, and simultaneous opposing decisions.
- Two isolated migration tests passed: latest migration on an empty SQLite database, and upgrade from `20260907121032_InitialIdentity` while preserving an existing account, password hash, approval status, and role assignment with new metadata left null.
- `dotnet-ef migrations has-pending-model-changes --no-build` passed outside the sandbox and confirmed that the EF model matches the tracked migration snapshot.
- A separate fresh-database bootstrap run applied both migrations and created exactly one Approved administrator with one Admin role. A second run made no changes and preserved its password hash and display name. The temporary database was removed.
- `dotnet list FitnessApp.slnx package --vulnerable --include-transitive` exited 0 and listed no vulnerable packages in all seven projects from its available data. Because the same command emitted NU1900 advisory-fetch errors for three projects, a complete current vulnerability audit remains unverified rather than claimed as passed.
- HTTPS browser verification at `https://localhost:7192` used an isolated SQLite database and synthetic test accounts. Verified: registration confirmation; Pending login denial; administrator login/navigation/list; approval; administrator logout; approved user login with no administrator navigation; rejection confirmation and rejection; Rejected login denial; empty list; and a stopped-server network error followed by successful retry after restart.
- Desktop login was visually inspected against the published prototype and exposed one focus-ring mismatch on the programmatically focused heading; `[tabindex="-1"]` focus styling was corrected while interactive focus indicators remain visible. A real narrow local-app viewport could not be selected through the available browser control, so local phone rendering remains a design-review gap; responsive CSS and the prototype phone layout were inspected, but that is not equivalent to a local phone browser pass.
- The local HTTPS verification server was stopped, its browser tabs were closed, and the database containing only synthetic test data was removed.

## Environment-specific findings

- Sandboxed WebAssembly builds can stall in the external `ComputeWasmBuildAssets` MSBuild task host. Serial builds outside that process restriction succeed without source changes using `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`, `MSBUILDDISABLENODEREUSE=1`, `-m:1`, and `-p:BuildInParallel=false`.
- EF migration generation inside the same sandbox showed the related task-host stall. Running the repository-local EF tool outside that restriction generated the migration normally. Two malformed untracked `bin\Debug` output directories created during the interrupted attempt were inspected and removed.
- NuGet advisory DNS resolution is intermittent in this environment, as described above. Package restore and compilation are otherwise successful.

## Deliberate limitations and merge gate

- Approval does not verify email ownership, and the application sends no email. Password reset is not implemented.
- Access tokens remain memory-only; reload, tab closure, or expiry requires login. Refresh tokens and remember-me are not implemented.
- No deployment, runtime secret vault, log shipping, Raspberry Pi/Docker setup, or fitness module was added.
- Pull request #1 is open. Its code revision passed the required PR workflow, but it must remain unmerged while the missing registration/administrator Figma frames and local phone-width browser pass remain unresolved design-verification gaps.
