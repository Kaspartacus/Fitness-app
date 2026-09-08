#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
solution="$repo_root/FitnessApp.slnx"
audit_parser="$script_dir/check-nuget-audit.py"

cd "$repo_root"

status() {
  printf '\n==> %s\n' "$1"
}

run_audit() {
  status 'Auditing direct and transitive NuGet packages'
  audit_dir="$(mktemp -d "${TMPDIR:-/tmp}/fitnessapp-audit.XXXXXX")"
  cleanup_audit() {
    rm -rf -- "$audit_dir"
  }
  trap cleanup_audit EXIT HUP INT TERM
  audit_json="$audit_dir/audit.json"
  audit_diagnostics="$audit_dir/diagnostics.txt"

  set +e
  dotnet package list \
    --project "$solution" \
    --vulnerable \
    --include-transitive \
    --format json \
    --output-version 1 \
    >"$audit_json" \
    2>"$audit_diagnostics"
  audit_exit="$?"
  set -e

  if [ "$audit_exit" -ne 0 ]; then
    printf 'Dependency audit incomplete: dotnet exited with status %s.\n' "$audit_exit" >&2
    parser_exit=3
  else
    set +e
    python3 "$audit_parser" "$solution" "$audit_json" "$audit_diagnostics"
    parser_exit="$?"
    set -e
  fi

  cleanup_audit
  trap - EXIT HUP INT TERM
  return "$parser_exit"
}

self_test() {
  status 'Testing structured dependency-audit classification'
  python3 "$audit_parser" --self-test
}

run_verify() {
  export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
  export MSBUILDDISABLENODEREUSE=1

  status 'Restoring repository-local tools'
  dotnet tool restore

  status 'Restoring dependencies serially'
  dotnet restore "$solution" --disable-parallel

  status 'Building serially'
  dotnet build "$solution" \
    --no-restore \
    --disable-build-servers \
    --verbosity minimal \
    -m:1 \
    -p:BuildInParallel=false

  status 'Running tests serially'
  dotnet test "$solution" \
    --no-build \
    --no-restore \
    --disable-build-servers \
    --verbosity normal \
    -m:1 \
    -p:BuildInParallel=false

  status 'Checking whitespace in tracked working-tree changes'
  git -C "$repo_root" diff --check
  git -C "$repo_root" diff --cached --check

  status 'Checking whitespace in untracked files'
  untracked_errors=0
  while IFS= read -r -d '' untracked_file; do
    whitespace_output="$(git -C "$repo_root" diff --no-index --check -- /dev/null "$repo_root/$untracked_file" 2>&1 || true)"
    if [ -n "$whitespace_output" ]; then
      printf '%s\n' "$whitespace_output" >&2
      untracked_errors=1
    fi
  done < <(git -C "$repo_root" ls-files --others --exclude-standard -z)

  if [ "$untracked_errors" -ne 0 ]; then
    return 1
  fi

  diff_base="${VERIFY_DIFF_BASE:-}"
  if [ -z "$diff_base" ] && git -C "$repo_root" rev-parse --verify --quiet origin/main >/dev/null; then
    diff_base='origin/main'
  fi

  if [ -n "$diff_base" ]; then
    status "Checking committed whitespace against $diff_base"
    git -C "$repo_root" diff --check "$diff_base"...HEAD
  else
    printf '%s\n' 'Committed diff whitespace was not checked: set VERIFY_DIFF_BASE or fetch origin/main.' >&2
    return 1
  fi

  printf '\n%s\n' 'Verification passed.'
}

case "${1:-verify}" in
  verify)
    run_verify
    ;;
  audit)
    run_audit
    ;;
  self-test)
    self_test
    ;;
  *)
    printf 'Usage: %s [verify|audit|self-test]\n' "$0" >&2
    exit 64
    ;;
esac
