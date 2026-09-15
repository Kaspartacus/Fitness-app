# Calendar feature checkpoint

Updated: 2026-09-15

## Objective

Extend the existing `/kalender` route with one-off, persisted moves for a specific planned running or strength occurrence; keep the recurring plan unchanged. Keep run registration scoped to a selected run activity, and show the shared mobile bottom navigation on the authenticated home page.

## Branch and worktree

- Branch: `feature/calendar`; worktree: `/Users/kaspartacuzz/Desktop/Fitness app/Fitness-app`.
- Existing open PR: [#11](https://github.com/Kaspartacus/Fitness-app/pull/11) targeting `main`. It has not been merged or deployed.

## Completed work (pushed)

- Added an owner-scoped `CalendarOccurrenceMove` persistence model and EF migration. A move retains the original scheduled date and overlays a target date, so it does not rewrite the running or strength recurring plan.
- Added owner-scoped calendar move endpoints and client calls. Running moves only apply to active, unstarted, result-free sessions in the plan period; strength moves validate the owned program, workout, and scheduled source day.
- Locked a running move with the same SQLite plan-write lock used by schedule regeneration. A moved source is preserved during running schedule edits, and a session start rechecks its effective target date after taking that lock.
- Updated the bounded calendar projection to suppress a source occurrence and render it once on its move target, with the original date displayed as context. Actual results stay on their actual dates; completed-result relationships use the moved planned date.
- Propagated a moved running occurrence’s effective date through plan, overview, list, detail, registration, active-session, start, and cancel mappings while retaining the stored source date for the schedule.
- Removed the generic selected-day `Registrer løb` action. Registration is only available from the selected planned run’s action dialog. Planned activities now open an action dialog with the specific run/strength move action, existing registration/start flow, and details.
- Reused the existing centered date picker as an accessible controlled move dialog, including validation, retryable server errors, cancellation handling, focus return, and calendar-state preservation.
- Added the existing shared bottom navigation to the authenticated home dashboard on mobile, with an active Hjem state; desktop keeps its existing desktop layout and hides the mobile navigation.
- Updated the calendar header: mobile hides its redundant back arrow, retains the working I dag action, and adds the existing home icon at the far right. Desktop keeps the back arrow.
- Corrected the calendar header action group so it no longer inherits the generic flexible header width; the home icon now aligns to the same right edge as on Løb.
- Used the user-provided screenshots and the existing documented design reference. Fresh Figma integration access was unavailable, so no exact Figma-frame verification is claimed.

## Verification

- `git diff --check` passes.
- The latest short serial build passed with 0 errors: `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 MSBUILDDISABLENODEREUSE=1 dotnet build FitnessApp.slnx --no-restore --disable-build-servers --verbosity minimal -m:1 -p:BuildInParallel=false`. It emitted one `MSB3026` static-web-assets retry warning from the active local server build path.
- No tests, app run, browser automation, audit, full verify script, database update, deployment, or CI change was performed.

## Preserved unrelated files and processes

- The untracked literal directories `src/FitnessApp.Infrastructure/bin\\Debug/` and `src/FitnessApp.Server/bin\\Debug/` are preserved and must not be staged or removed.
- A user-owned `dotnet run --project src/FitnessApp.Server --launch-profile https` process was detected and left untouched.

## Delivery

- Commit `97a8559` (`feat: move individual calendar activities`) is pushed on `feature/calendar`.
- Commit `e10e77c` (`fix: align calendar mobile header`) is pushed on `feature/calendar`.
- PR [#11](https://github.com/Kaspartacus/Fitness-app/pull/11) is open from `feature/calendar` to `main`. It has not been merged or deployed.

## Exact next action

No code action remains. The next action is user manual verification that the calendar home icon aligns with Løb on mobile, alongside the existing calendar flows. Do not merge or deploy.
