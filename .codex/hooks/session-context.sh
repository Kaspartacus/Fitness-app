#!/usr/bin/env bash
set -eu

if ! repo_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf '%s\n' 'FitnessApp resume context unavailable: current directory is not a Git worktree.'
  exit 0
fi

branch="$(git -C "$repo_root" branch --show-current)"
if [ -n "$(git -C "$repo_root" status --porcelain --untracked-files=normal)" ]; then
  dirty='yes'
else
  dirty='no'
fi

checkpoint="$repo_root/.codex/checkpoint.md"
if [ -f "$checkpoint" ]; then
  checkpoint_state='.codex/checkpoint.md (present)'
else
  checkpoint_state='not present'
fi

printf 'FitnessApp resume context:\n- Branch: %s\n- Worktree changes: %s\n- Checkpoint: %s\n' \
  "${branch:-detached HEAD}" "$dirty" "$checkpoint_state"
