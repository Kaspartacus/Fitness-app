# Calendar feature checkpoint

Updated: 2026-09-13

## Objective

Complete the existing `/kalender` route as the private shared running and strength calendar, retaining the existing single .NET/Blazor solution and its established flows.

## Branch and worktree

- Branch: `feature/calendar`, based on `origin/main` at `ed99839`; worktree: `/Users/kaspartacuzz/Desktop/Fitness app/Fitness-app`.
- No new solution, host, competing calendar route, migration, test run, application run, browser check, audit, deployment, or CI change was made.

## Completed work (uncommitted)

- Added an owner-scoped, inclusive, maximum-93-day calendar API and client. It queries active planned running sessions, actual running results (including manual and retired-plan history), current/future strength schedule occurrences, and actual completed strength snapshots without one request per day.
- Kept planned and actual dates separate. Running results render on their actual date; when it differs, the scheduled date displays a non-counting relationship to the single result. Strength’s weekly model is projected only from Copenhagen-local today forward, so it never invents historical scheduled occurrences.
- Replaced the existing running-only calendar UI in place with month navigation, Monday-first grid, today and selected-day states, accessible keyboard selection, activity indicators, loading/empty/error/retry states, and state-preserving links to existing running/strength flows. A read-only owned completed-strength detail route was added because an active workout route would imply a new session.
- Used `TimeProvider` and `Europe/Copenhagen` for the new strength calendar-facing dates and completion timestamps. No persistence schema change was necessary.
- Updated project/progress documentation. Figma’s prior source inventory included the calendar screen; the integration was unavailable after interruption, so no fresh exact-frame or runtime visual verification is claimed.

## Verification

- `git diff --check` passes.
- The replacement short serial build, `dotnet build FitnessApp.slnx --no-restore --disable-build-servers --verbosity minimal -m:1 -p:BuildInParallel=false`, succeeded with 0 errors. It emitted one `NU1900` warning because this environment could not resolve NuGet's advisory endpoint. The first sandboxed attempt failed only at the known WebAssembly task-host boundary (`MSB4216`); the identical approved non-sandboxed build completed successfully.
- Per owner instruction: no tests, app run, browser automation, audit, or full verification script.

## Preserved unrelated files

- The interrupted build left untracked literal directories `src/FitnessApp.Infrastructure/bin\\Debug/` and `src/FitnessApp.Server/bin\\Debug/`. They are not staged, changed, or removed.

## Delivery

- Commit `baa036dd1159f97a860207d93b200b5847fc94af` (`feat: complete shared calendar`) is pushed on `feature/calendar`.
- Pull request [#11](https://github.com/Kaspartacus/Fitness-app/pull/11) targets `main`, is open, and has not been merged or deployed.

## Exact next action

- Owner manual checks may cover month/date navigation, planned and completed running/strength activities, retained history, and a planned-versus-actual date mismatch. Do not merge or deploy from this checkpoint.
