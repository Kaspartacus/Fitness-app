# FitnessApp Codex entrypoint

Work in the existing repository at `Desktop/Fitness app/Fitness-app`. Confirm `git rev-parse --show-toplevel` before editing. Extend its single `FitnessApp.slnx`, six `src/` projects, and one Server host. Do not create another solution, application, or ordinary feature worktree. Preserve unrelated work.

Read only the guidance relevant to the task:

- [Agent OS](.ai/agent-os/workflow.md): task interpretation, routing, validation, and handoff.
- [Harness](.ai/harness/development.md): basic development rules; also consult coding, testing, or safety guidance when relevant.
- [Context](.ai/context/overview.md): selective project-specific implementation context.
- [Agents](.ai/agents/README.md): role boundaries and Codex-native agent locations.
- [Skills](.ai/skills/README.md): reusable Codex-native procedures.
- Applicable nested `AGENTS.md` in the project or test directory being changed.

Code, configuration, migrations, and tests establish current executable behavior. The private `personal` Obsidian vault can add broader product history when accessible; work must remain possible without it. Never put secrets or personal data in repository guidance or vault notes.

Keep code and technical documentation English; keep application UI Danish. Derive ownership and roles from validated server identity. Preserve recorded history when definitions change. Use [development workflow](docs/development-workflow.md) for checkout safety, verification, PRs, and review.

When the entire user message is exactly `Sæt i gang`, use the `cleanup_maintainer` custom agent and `$fitness-cleanup-maintenance` skill. That exact trigger authorizes its cleanup branch, commit, push, and PR workflow, never merge or deployment. A longer message containing these words is not the trigger.

Every feature PR follows `.agents/skills/fitness-pr-review/SKILL.md`. On each automatic review or `@codex review` request, review the latest PR head and post a new top-level GitHub comment using that procedure. The implementing agent follows `$fitness-pr` and posts one `@codex review` trigger after each agent-initiated PR push. Do not message another local Codex task for this handoff. Report demonstrated consequential defects; CI handles deterministic checks.
