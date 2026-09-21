# Client design reference

Use this record when changing the Blazor client. The visual source is the [FitnessApp Figma Make file](https://www.figma.com/make/dFJcR42XWiqVhBtA1bfyOS/Fitness-app?p=f&t=3UwAWBKvTu85DryU-0) and its [published prototype](https://trance-vine-53032594.figma.site/). It is design evidence, not a complete functional specification: controls, states, and flows may be absent or nonfunctional, and returned React source must be translated into Blazor.

## Recorded evidence

- The shared direction is charcoal and gray surfaces, filled navy primary actions, navy/light-blue active and focus states, and readable gray-blue inactive states. Reuse existing Blazor CSS tokens, page patterns, and the pulse-mark asset before adding visual primitives.
- The authentication review recorded a login screen but no matching registration, confirmation, or administrator-review frames. Those screens are consistent extensions, not pixel-verified copies. The Make canvas preview was unavailable during that review.
- The strength review covered the overview, program details, weekly planning bottom sheet, active workout, and completion dialog. The current centered dialog is a deliberate accessibility extension for keyboard focus, scrolling, and desktop use. Warm-up is an ordinary exercise, not a stored boolean.
- The running review covered the overview, setup, plan and session details, active run, manual logging, result detail, date selection, and calendar connection. Centered dialogs, mobile-only bottom navigation, Danish feedback states, cancellation guards, and confirmations are deliberate extensions. The overview intentionally omits **Seneste løb**; **Resultathistorik** reopens manual and archived-plan results.

## Applying evidence

For a UI slice, record whether it was directly verified, consistently extended from verified patterns, or unavailable. Exercise the real HTTPS app at desktop and mobile widths and check long content, focus, touch targets, validation, loading, empty, denied, success, and recoverable-failure states relevant to the change. Do not claim pixel parity when matching current evidence is absent or unavailable.
