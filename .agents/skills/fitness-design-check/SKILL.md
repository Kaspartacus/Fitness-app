---
name: fitness-design-check
description: Verify a FitnessApp UI implementation against current design evidence and the running Blazor application. Use for visual fidelity, responsive behavior, interaction-state, and accessibility checks. Do not use for general feature implementation, code review, or pull-request preparation.
---

# Fitness Design Check

Check the real application against the best available current source without overstating fidelity.

## Inputs

- The changed routes, components, and expected user journey.
- Existing CSS tokens, shared components, assets, screenshots, and documented design links.
- The current design source only when it is available and relevant.

## Workflow

1. Inspect related existing screens, shared components, CSS tokens, and assets before changing visual language.
2. Establish the evidence level for each screen:
   - **Verified:** compared with a current matching design frame or source.
   - **Consistent extension:** no matching frame exists, so existing verified patterns are reused.
   - **Unavailable:** the source could not be accessed; state the blocker.
3. Reuse the project's real components, assets, icons, and tokens. Translate design intent into Blazor/CSS; do not add React, Tailwind, or duplicate prototypes.
4. Run the actual HTTPS app and exercise the relevant journey, not only isolated screenshots.
5. Check representative desktop and mobile widths, including 390 px and 360 px when applicable. Inspect clipping, horizontal overflow, long content, touch targets, focus visibility, validation, loading, empty, success, denied, and recoverable error states.
6. Compare source and implementation at the same viewport when matching evidence exists. Fix concrete discrepancies within scope and recheck.
7. Inspect browser console output and protected navigation behavior when tooling permits.

## Guardrails

- Never modify the Figma file unless explicitly authorized.
- Do not claim pixel parity for absent, stale, or failed design evidence.
- Do not invent a new visual system when the repository already supplies one.
- Keep UI text Danish and record environment failures separately from product defects.

## Completion

Return tested routes, viewport sizes, interaction states, console result, concrete fixes, screenshots where useful, evidence classification, and any checks that could not be performed.
