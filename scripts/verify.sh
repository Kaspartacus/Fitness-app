#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
solution="$repo_root/FitnessApp.slnx"

status() {
  printf '\n==> %s\n' "$1"
}

classify_audit() {
  audit_file="$1"

  if grep -Eq 'NU1900|Error occurred while getting package vulnerability data|Unable to load the service index|No such host is known|Name or service not known' "$audit_file"; then
    printf '%s\n' 'Dependency audit incomplete: advisory data could not be retrieved.' >&2
    return 3
  fi

  if grep -q 'has the following vulnerable packages' "$audit_file"; then
    printf '%s\n' 'Dependency audit found vulnerable packages.' >&2
    return 2
  fi

  clean_projects="$(grep -c 'has no vulnerable packages' "$audit_file" || true)"
  if [ "$clean_projects" -eq 7 ]; then
    printf '%s\n' 'Dependency audit complete: no known vulnerable packages in all seven projects.'
    return 0
  fi

  printf '%s\n' 'Dependency audit incomplete: output did not confirm all seven projects.' >&2
  return 3
}

run_audit() {
  status 'Auditing direct and transitive NuGet packages'
  audit_log="$(mktemp "${TMPDIR:-/tmp}/fitnessapp-audit.XXXXXX")"
  trap 'rm -f -- "$audit_log"' RETURN

  set +e
  # Deliberately allow this command to restore so standalone audits cannot use a
  # stale assets file after a package-reference change.
  dotnet list "$solution" package --vulnerable --include-transitive 2>&1 | tee "$audit_log"
  audit_exit="${PIPESTATUS[0]}"
  set -e

  if [ "$audit_exit" -ne 0 ]; then
    printf 'Dependency audit incomplete: dotnet exited with status %s.\n' "$audit_exit" >&2
    return 3
  fi

  classify_audit "$audit_log"
}

self_test() {
  status 'Testing dependency-audit result classification'
  fixture_dir="$(mktemp -d "${TMPDIR:-/tmp}/fitnessapp-verify.XXXXXX")"
  trap 'rm -rf -- "$fixture_dir"' RETURN

  for project_number in 1 2 3 4 5 6 7; do
    printf 'Project %s has no vulnerable packages given the current sources.\n' "$project_number"
  done > "$fixture_dir/clean.txt"
  printf '%s\n' 'Project has the following vulnerable packages' > "$fixture_dir/findings.txt"
  printf '%s\n' 'warning NU1900: Error occurred while getting package vulnerability data' > "$fixture_dir/incomplete.txt"

  classify_audit "$fixture_dir/clean.txt"

  set +e
  classify_audit "$fixture_dir/findings.txt" >/dev/null 2>&1
  findings_exit="$?"
  classify_audit "$fixture_dir/incomplete.txt" >/dev/null 2>&1
  incomplete_exit="$?"
  set -e

  if [ "$findings_exit" -ne 2 ] || [ "$incomplete_exit" -ne 3 ]; then
    printf 'Dependency-audit classifier self-test failed (findings=%s, incomplete=%s).\n' \
      "$findings_exit" "$incomplete_exit" >&2
    return 1
  fi

  printf '%s\n' 'Classifier self-test passed: findings and incomplete advisory data both fail nonzero.'
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
