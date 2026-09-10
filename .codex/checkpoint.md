# Running-module checkpoint

Updated: 2026-09-10

## Objective

Implement the complete private running vertical slice within the existing FitnessApp solution: Figma-informed Danish screens, user-owned plans and sessions, manual and planned-run results, deterministic safe plan generation, calendar entry points, and the required persistence migration follow-up.

## Current running implementation record

- Branch: `feature/running`; checkout: `/Users/kaspartacuzz/Desktop/Fitness app/Fitness-app`. The existing `FitnessApp.slnx` and `FitnessApp.Server` remain the only solution and hosted startup project.
- Figma Make version 47 and its published public preview were reviewed as visual/journey reference. The inspected journey covers overview, plan setup, plan/session detail, active run, manual registration, saved-result detail, date selection, and calendar access. It is not treated as a complete behavioral specification or as runtime/pixel-parity evidence.
- The complete client flow is present at `/loeb` (overview), `/loeb/ny` (setup), `/loeb/plan` (week plan), `/loeb/plan/{sessionId}` (session detail), `/loeb/plan/{sessionId}/aktiv` (active timer), `/loeb/registrer` and `/loeb/plan/{sessionId}/registrer` (manual/planned registration), `/loeb/resultater` (result history), `/loeb/resultater/{resultId}` plus `/ret` (result detail/correction), and `/kalender` (month/day selection). The journey is empty overview → setup or manual registration → active plan and weekly sessions → selected session → start/active timer or log/complete → saved-result detail and correction. Result history reopens every manual and archived-plan result; a selected calendar day routes to the relevant scheduled session or result. The overview deliberately does not restore **Seneste løb**, because the current reference does not include it.
- Running data is private to the approved JWT subject. Plans hold the chosen level, 30-minute ability, target distance/date, frequency, and weekdays; sessions hold date, kind, planned distance, pace range, structure, and an optional server-recorded start; results hold actual distance/duration/heart rate/note as appropriate. Planned targets and actual measurements are separate. Running API responses are `no-store`.
- Calendar-facing dates use `DateOnly` with an explicit `Europe/Copenhagen` interpretation derived from `TimeProvider`; the client also derives its displayed/current day and date constraints in Copenhagen so the journeys do not diverge near midnight. Setup accepts the five defined levels, 0.5–15 km in 30 minutes, a 1–100 km target, a target date 7–365 days ahead, and exactly the selected one-to-seven weekday frequency. The local generator schedules every selected weekday through the target date, evaluates the target against the final scheduled session's capacity, caps compounded weekly capability growth at 10%, and rejects an infeasible target/date/frequency instead of copying prototype sessions. Replacing a plan requires confirmation plus matching ID/version and retires the prior active plan transactionally while keeping sessions/results/history.
- The active timer is calculated from the persisted server start timestamp and the current clock. It is intentionally only elapsed time: no GPS, maps, background tracking, sensor data, or fabricated live pace/distance/heart-rate values are presented. A planned completion/correction can have no actual measurements; manual registration/correction requires real distance and duration. Pace is calculated only from real non-zero distance and duration.
- Loading, empty, validation, replacement confirmation, active-run cancellation, stale-write conflict, error/retry, and draft-preservation states extend the Figma screens with existing shared modal and UI patterns. Completion is idempotent per owner and result correction uses a version value; starts are idempotent for active non-future sessions, each plan permits only one started/uncompleted session, cancellation only clears an uncompleted active start, and calendar ranges are inclusive with a 93-day maximum. A future calendar day intentionally hides manual registration rather than silently defaulting a result to today.

## Running migration

- `20260910221108_AddRunningModule` and the matching model snapshot are generated and reviewed. They have not been applied to the configured local SQLite database. Apply them deliberately when ready:

  ```bash
  dotnet tool run dotnet-ef database update \
    --project src/FitnessApp.Infrastructure \
    --startup-project src/FitnessApp.Server
  ```

## Running verification record

- Do not infer a running result from the historical strength verification below. The requested delivery excludes application launch, browser checking, automated tests, dependency audit, and the full verification script. `dotnet build FitnessApp.slnx --no-restore --disable-build-servers --verbosity minimal -m:1 -p:BuildInParallel=false` succeeded with zero errors. It emitted one `NU1900` warning because the sandbox could not resolve `api.nuget.org` for vulnerability metadata; no package restore was performed.
- Source inspection and a compilation result do not establish runtime behavior, persistence migration application, or exact Figma fidelity. Those remain explicit manual follow-up checks.

## Next action

- Complete the owner-requested commit/push/PR handoff without applying the generated migration to the local database.

## Historical strength-work record

- Branch: `feature/strength-workouts`; base commit: `d6345d6`.
- Checkout: `/Users/kaspartacuzz/Desktop/Fitness app/Fitness-app`.
- One solution (`FitnessApp.slnx`) and one hosted startup project (`FitnessApp.Server`) remain. No additional solution, project, executable, host, or worktree was created.

### Completed, uncommitted work

- Replaced the former flat strength model with program → ordered workouts → ordered exercises in Domain, Application, Contracts, Infrastructure, Server, and Client.
- Added Figma-aligned Danish strength overview, program details, program/workout editing, a centered week-plan dialog, active workout with steppers and completion confirmation, loading/empty/error states, and live previous-performance display.
- Added `20260910191738_AddStrengthTrainingFlow`, a data-preserving SQLite rebuild that migrates each former direct program into `Træning 1`, then adds scheduling and completed-workout tables.
- Applied that migration to the configured local database after copying it to `/private/tmp/fitnessapp-before-strength-flow-20260910.db`. The existing `Ben` program and three exercises are retained; foreign-key validation is clean.
- Repaired the Server static-asset pipeline for .NET 10 WebAssembly (`UseRouting`, `MapStaticAssets`, and endpoint execution) and marks the HTML shell `no-cache`, so a fresh build cannot leave the browser on an old fingerprinted client file.
- Updated integration coverage for hierarchy, ordering, notes, warm-up removal, validation, ownership, schedule, completion/history, live session checks, stale writes, and migration preservation.
- Updated README and design/project/progress documentation to describe the actual Figma flow.
- Replaced all strength bottom sheets and inline deletion confirmations with the shared `ModalDialog` component and its client focus/scroll helper. The component is also used for the existing administrator rejection confirmation.
- Reworked workout editing around compact exercise cards and a modal add/edit form. Cards now expose keyboard-accessible edit behavior, reorder icons, icon-only deletion, and named deletion confirmation. Program detail now supports confirmed workout deletion through the existing optimistic save flow.
- Program and workout drafts remain available after failed saves, and their internal back/cancel controls ask before discarding unsaved work. An edited exercise modal asks before it discards its temporary values. Busy destructive dialogs cannot be dismissed until the request returns.
- Made the Figma five-item bottom navigation reusable and mobile-only. Desktop uses a wider readable content column with no fixed navigation or reserved bottom gap.

### Verification so far

- `dotnet build FitnessApp.slnx --no-restore`: passed; only `NU1900` because this environment cannot resolve NuGet’s advisory endpoint.
- `dotnet test FitnessApp.slnx --no-build --no-restore --disable-build-servers --verbosity normal -m:1 -p:BuildInParallel=false`: **86 passed, 0 failed, 0 skipped**.
- `PRAGMA foreign_key_check`: clean against the repaired local SQLite database.
- `dotnet run --project src/FitnessApp.Server --launch-profile https --no-build`: starts on `https://localhost:7192`. A fresh browser session loads the WebAssembly client and routes unauthenticated `/styrke` to the working login page; the current fingerprinted runtime asset returns HTTP 200.
- Separate correctness and security rechecks found no remaining concrete issue after the validation and migration fixes.

### Delivery status

- Feature commit `ff2f0fdbf17a424f2029edec512674a4199ea4fe` is pushed on `feature/strength-workouts`.
- Pull request [#8](https://github.com/Kaspartacus/Fitness-app/pull/8) targets `main`; it has not been merged or deployed.
- The owner requested no application run, browser verification, tests, audit, or shared verification script. The single permitted `dotnet build FitnessApp.slnx --no-restore` passed with no errors; the only output was the environment's known `NU1900` advisory-DNS warning.

Never store credentials, tokens, personal data, reset URLs, or conversation transcripts here.

### Later strength-integrity record

- Branch: `fix/strength-data-integrity`; base commit: `750a35ca943c2ba7ec6b6bcd2f6edab40a7479ae` (`origin/main`).
- Completed and committed: completed-only previous performance; shared-modal internal navigation guard for active workouts; owner-scoped idempotent completion IDs and data-preserving migration; a single aggregate schedule/version read.
- The migration assertion now includes `20260910210000_AddCompletedWorkoutCompletionId`. The performance regression test uses distinct values to verify that an earlier completed result is retained after a later skip while another newly completed exercise becomes the latest result.
- No application run, browser verification, or audit was run. The two focused tests passed, then the full suite passed: 86 passed, 0 failed, 0 skipped.
- Test repair commit `08039cb` is pushed without force. Pull request [#9](https://github.com/Kaspartacus/Fitness-app/pull/9) targets `main`; it has not been merged or deployed.
