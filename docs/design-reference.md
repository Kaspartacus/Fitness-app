# Design reference

The current visual source and verified limitations are recorded here so feature instructions remain concise.

- Primary source: [FitnessApp Figma Make file](https://www.figma.com/make/dFJcR42XWiqVhBtA1bfyOS/Fitness-app?p=f&t=3UwAWBKvTu85DryU-0).
- Current inspected Make version: 41.
- Direction: charcoal and gray surfaces, filled navy primary actions, navy/light-blue active and focus states, and readable gray-blue inactive states.
- Reuse the existing Blazor CSS tokens, shared page patterns, and pulse-mark asset before adding new visual primitives.
- The inspected source inventory includes the login screen but not registration, confirmation, or administrator-review frames. Those current screens are consistent extensions, not pixel-verified copies.
- The Make live preview was unavailable during the 2026-09-08 verification. Treat that as unavailable external evidence, not a product-code failure.
- On 2026-09-09 the published prototype was accessible and the strength overview, program details, staged creation flow, and exercise editor were inspected directly. The implemented slice reuses its compact charcoal cards, blue primary actions, manual exercise names, and visible 1–10 set / 1–30 repetition limits. The prototype models multiple named workouts inside one program, while this first persisted slice follows the agreed simpler program-with-exercises data model.
- No warm-up control appeared in the inspected prototype exercise editor. The implemented per-exercise **Opvarmning** checkbox is therefore a documented product-spec extension rather than a pixel-verified match. Prototype-only **Start** and **Planlæg** actions are omitted until their flows exist.

For each UI slice, record whether it was directly verified, consistently extended from verified patterns, or could not be checked. Exercise the real HTTPS app at desktop and mobile widths, including long content, focus, touch targets, validation, loading, empty, denied, and failure states relevant to the change.
