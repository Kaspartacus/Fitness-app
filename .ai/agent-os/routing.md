# Advisory Codex routing

This mapping is guidance for choosing a model and reasoning effort; Markdown does not switch the running model or spawn agents. Availability and overrides depend on the Codex host. Prefer the least costly configuration that is sufficiently capable, and escalate when evidence warrants it. The user can override complexity or model choice.

| Tier | Typical use | Suggested configuration |
| --- | --- | --- |
| small | Clear, scoped, low-risk edits; finalization | Codex Luna, low effort |
| standard | Ordinary implementation and review | Codex Sol, medium effort |
| strong | Architecture, difficult reasoning, or consequential risk | Codex Astra, high effort |

Complexity does not erase risk: a small auth edit can need a strong security review. A normal task may use standard Developer and strong Reviewer; use small Finalizer when appropriate. If a standard pass fails twice on a substantive issue, ask the user about escalation or unresolved decisions. Actual model choices require an available Codex model override or user selection. No automatic runtime router is installed.
