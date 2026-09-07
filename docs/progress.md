# Progress

Last updated: 2026-09-07

## Completed authentication slice

- Added the concrete `FitnessApp.Contracts` project for login and authenticated-user HTTP DTOs without exposing Infrastructure types to Client.
- Added `Pending`, `Approved`, and `Rejected` account approval states in Domain.
- Added EF Core SQLite and ASP.NET Core Identity persistence in Infrastructure, including users, roles, lockout configuration, persisted sessions, path-safe database configuration, and the tracked `20260907121032_InitialIdentity` migration.
- Added `Admin` and `User` roles. Only currently Approved accounts can receive a token or pass protected-request validation.
- Added the explicit `bootstrap-admin` command. It reads credentials from server-side configuration/User Secrets, has no HTTP route or password argument, creates an Approved Admin once, refuses to elevate an existing account, and makes no changes after initialization.
- Added maintained JwtBearer/JWT libraries and signed HS256 access tokens. Configuration validation rejects missing/invalid signing keys and invalid issuer, audience, lifetime, or skew configuration. Validation requires a signed token, HS256, issuer, audience, expiry, persisted active session, and current Approved status.
- Added `/api/auth/login`, `/api/auth/me`, and `/api/auth/logout`, no-store authentication responses, generic Danish login failures, Identity lockout, IP-partitioned login rate limiting, correct 401/403 behavior, and an API fallback that prevents unknown `/api` routes from returning `index.html`.
- Added structured JSON console logging with event IDs 1000-1003 and 5000 plus trace/request scopes. Credential values, bearer tokens, signing keys, and request bodies are not logged; EF sensitive-data logging is not enabled.
- Added a Danish accessible login form, loading/error states, protected home page, authenticated display name, logout, and invalid-session redirect behavior. Recoverable home-load failures now finish loading and offer a duplicate-safe retry. Logout clears memory locally for success, 401, server errors, and network errors; an unconfirmed server logout is stated explicitly on the login page.
- Tokens remain in client memory and are attached only to same-origin `/api/` requests. Automatic 401 clearing/navigation is restricted to the same API, while login 401 remains a form error and external responses cannot clear the session.
- Added 23 integration and lightweight client tests backed by isolated SQLite databases and test-only generated secrets. Coverage includes approved admin login, generic invalid credentials, Pending/Rejected denial, missing/expired/tampered/wrong-issuer/wrong-audience/wrong-algorithm token rejection, valid protected access, Admin-policy denial, live role revocation, server-side logout invalidation, client logout failure outcomes, recoverable current-user retry, same-origin/external handler boundaries, live approval revocation, safe bootstrap/elevation refusal, lockout, rate limiting, and unknown API routing.
- Added a repository-local `dotnet-ef` tool manifest and ignored SQLite database and sidecar files while keeping migrations tracked.

## Final corrective findings

- The wrong-algorithm fixture initially changed both algorithm and signing key. It now uses one generated 512-bit signing key for the server, an accepted HS256 token, and an otherwise-valid rejected HS384 token, isolating explicit allowed-algorithm enforcement. Production issuance and validation remain HS256-only.
- Tampering with the last Base64URL character was flaky because unused padding bits can change without changing decoded signature bytes. The test now changes the first signature byte deterministically.
- The Server project briefly contained a duplicate Contracts reference. The duplicate was removed.
- Generated `bin`, `obj`, malformed `bin\Debug`, and recursively nested build output were removed. A subsequent clean build did not recreate the malformed or recursive paths.
- `InputText` with an overridden `oninput` event produced a runtime `ChangeEventArgs`/`string` mismatch. The two fields now use native Blazor-bound inputs with `@bind:event="oninput"`, retaining `EditForm` and DataAnnotations validation.
- `IHttpClientFactory` creates message handlers in a separate DI scope. A scoped memory-token provider therefore gave the handler a different instance from the UI. The browser-local provider is now explicitly a Client singleton, so both UI authorization and the same-origin API handler use the same in-memory token. No persistent browser storage was introduced.
- Login now uses ordinary internal Blazor navigation to the protected home route. Logout still replaces the history entry to avoid returning to an authenticated view.
- HTTP responses from login/current-user/logout calls are disposed. Recoverable current-user failures no longer collapse into the invalid-session path, and failed server logout cannot strand a disabled button or imply that revocation succeeded.
- The test-only Admin-policy endpoint returns HTTP 200 on authorized success so live role revocation is verified precisely as 200 → 403 with the exact same JWT, while `/api/auth/me` remains 200 for the still-Approved account.
- README secret setup now uses a `zsh` masked prompt, OpenSSL-generated 384-bit key, Python JSON encoding, and `dotnet user-secrets set` standard input. An isolated temporary User Secrets id verified the syntax and preservation of a pre-existing entry; only that task-created id was cleared.

## Figma access evidence

The required single design-context attempt for Make file key `dFJcR42XWiqVhBtA1bfyOS`, node `0:1`, again returned only a source/resource listing (including `LoginScreen.tsx`) and no readable source contents or rendered screenshot. No repeat request was made and Figma was not modified. The functional interim UI reuses the existing charcoal/mint styles. Detailed visual matching remains unverified and requires working source/visual access or user-provided screenshots/exports.

## Verification

- `dotnet tool restore`: succeeded; restored `dotnet-ef` 10.0.3.
- Serial `dotnet restore FitnessApp.slnx`: succeeded for all seven projects. Its automatic vulnerability metadata request temporarily failed DNS resolution and emitted NU1900 for Client, Infrastructure, and Server; package restore itself succeeded.
- Final serial `dotnet build FitnessApp.slnx --no-restore`: succeeded in 4.04 seconds with 0 errors. Its three warnings were the cached NU1900 metadata failures only; no compiler or source warning remained.
- Final `dotnet test FitnessApp.slnx --no-build --no-restore`: passed 23 of 23 tests, 0 failed, 0 skipped, in 2 seconds.
- Explicit `dotnet list FitnessApp.slnx package --vulnerable --include-transitive --no-restore`: completed successfully against `https://api.nuget.org/v3/index.json` and reported no known vulnerable direct or transitive packages in all seven projects.
- The initial migration was applied successfully to a fresh isolated SQLite database.
- First administrator bootstrap created exactly one user named `Testadministrator` with approval value `Approved` and role `Admin`. A second run used a different generated test password and display name, reported that bootstrap was already complete, and left both the password hash and display name unchanged. Existing-account elevation refusal also passed in the integration suite.
- HTTPS browser verification succeeded at `https://localhost:7192` with an isolated SQLite database and generated one-time credentials: the Danish login form loaded, live `oninput` fields submitted successfully, `/` showed the protected greeting, and normal logout returned to `/login`. After a second login, stopping only the verification server forced the network-failure path; logout still cleared local state, navigated to `/login?logout=unconfirmed`, showed `Du er logget ud lokalt. Serverlogout kunne ikke bekræftes.`, and left no browser warning/error. The temporary database and credential file were removed afterward.
- The local HTTPS server was stopped after verification and browser test tabs were finalized.

## Environment-specific issues

- Sandboxed builds stalled while launching the external `ComputeWasmBuildAssets` MSBuild task host. The same clean, serial build completed normally outside that process restriction without a source workaround. `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`, `MSBUILDDISABLENODEREUSE=1`, `-m:1`, and `-p:BuildInParallel=false` kept final verification isolated from an unrelated Rider MSBuild process.
- Restore/build intermittently could not resolve `api.nuget.org` for automatic NuGet audit metadata and therefore retained NU1900 warnings. The immediately subsequent explicit direct/transitive audit did reach the same configured source and completed successfully; this is an environment/network inconsistency, not a product-code failure.

## Deliberate limitations and next slice

- The access token is held only in browser memory. Reload, tab closure, or token expiry requires login again. Refresh tokens and remember-me behavior are not implemented.
- Registration, approval endpoints/UI, password reset, deployment, CI/CD, vaults, and log aggregation are not implemented.
- The next slice is registration plus administrator approval. Password-reset delivery still requires a decision before implementation.
