# Design reference

The current visual source and verified limitations are recorded here so feature instructions remain concise.

- Primary source: [FitnessApp Figma Make file](https://www.figma.com/make/dFJcR42XWiqVhBtA1bfyOS/Fitness-app?p=f&t=3UwAWBKvTu85DryU-0).
- Current inspected Make version: 44.
- Direction: charcoal and gray surfaces, filled navy primary actions, navy/light-blue active and focus states, and readable gray-blue inactive states.
- Reuse the existing Blazor CSS tokens, shared page patterns, and pulse-mark asset before adding new visual primitives.
- The inspected source inventory includes the login screen but not registration, confirmation, or administrator-review frames. Those current screens are consistent extensions, not pixel-verified copies.
- The Make live preview was unavailable during the 2026-09-08 verification. Treat that as unavailable external evidence, not a product-code failure.
- On 2026-09-10 the Make file’s strength screens and source were directly inspected: overview, program details, weekly planning bottom sheet, active workout, and its completion dialog. The translated UI uses the evidence’s #0B1118 background, #151E29 surfaces, #1C2836 raised controls, #34465A borders, #24558B primary actions, #9BC5F4 accents, narrow mobile canvas, fixed bottom navigation, upper-left back controls, compact workout cards, and stepper controls. The current shared, centered dialog is a deliberate extension requested for keyboard, focus, scrolling, and desktop accessibility; it replaces the Figma bottom sheet in interactive flows.
- The implemented flow is program → ordered workouts → ordered exercises. It includes **Start træning**, **Planlæg**, today’s scheduled workout, weekly rest-day/workout selection, actual completion data and latest prior performance. No warm-up control appeared in the exercise editor: warm-up is an ordinary exercise, with no persisted boolean.

For each UI slice, record whether it was directly verified, consistently extended from verified patterns, or could not be checked. Exercise the real HTTPS app at desktop and mobile widths, including long content, focus, touch targets, validation, loading, empty, denied, and failure states relevant to the change.
