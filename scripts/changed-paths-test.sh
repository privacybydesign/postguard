#!/usr/bin/env bash
#
# Regression tests for scripts/changed-paths.sh, covering the exit-code
# contract: 0 means the question was answered (true or false), 2 means the
# script could not find out. A job that read a 2 as "nothing changed" would
# skip its gate, which is the failure this pins.
#
# Entirely offline: the changed files come from a fixture file, so `gh` is
# never called.
#
# Usage:
#   scripts/changed-paths-test.sh
#
set -euo pipefail

root=$(git rev-parse --show-toplevel)
cd "$root"

gate="$root/scripts/changed-paths.sh"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

pass=0
fail=0

wire=$'wire:\n  - \'pg-core/**\'\n  # a comment between patterns\n  - \'Cargo.lock\'\n  - \'.github/workflows/build.yml\''

# check <description> <expected exit> <expected stdout> <filters> [changed files...]
# With no changed files given, CHANGED_FILES is left unset.
check() {
  local desc=$1 want_code=$2 want_out=$3 filters=$4
  shift 4
  local code=0 out
  if [[ $# -gt 0 ]]; then
    printf '%s\n' "$@" >"$tmp/changed"
    out=$(env -u GITHUB_OUTPUT FILTERS="$filters" CHANGED_FILES="$tmp/changed" \
      GITHUB_EVENT_NAME=pull_request "$gate" 2>/dev/null) || code=$?
  else
    out=$(env -u GITHUB_OUTPUT -u CHANGED_FILES FILTERS="$filters" \
      GITHUB_EVENT_NAME="${EVENT:-push}" "$gate" 2>/dev/null) || code=$?
  fi
  if [[ $code -eq $want_code && $out == "$want_out" ]]; then
    pass=$((pass + 1))
    echo "ok   $desc"
  else
    fail=$((fail + 1))
    echo "FAIL $desc: exit $code (want $want_code), output '$out' (want '$want_out')"
  fi
}

check "a file under a dir/** pattern matches" 0 "wire=true" "$wire" pg-core/src/lib.rs
check "a nested file under a dir/** pattern matches" 0 "wire=true" "$wire" pg-core/tests/a/b.rs
check "an exact path matches" 0 "wire=true" "$wire" README.md Cargo.lock
check "a dotfile path matches" 0 "wire=true" "$wire" .github/workflows/build.yml
check "an unrelated change is false, not an error" 0 "wire=false" "$wire" README.md pg-pkg/src/main.rs
check "a sibling sharing the directory's prefix does not match" 0 "wire=false" "$wire" pg-core-extras/x.rs
check "an exact path does not match as a prefix" 0 "wire=false" "$wire" Cargo.lock.bak
check "an exact path does not match in a subdirectory" 0 "wire=false" "$wire" cryptify/Cargo.lock
check "an empty change list is false" 0 "wire=false" "$wire" ""
check "a push has no pull request to compare, so the job runs" 0 "wire=true" "$wire"
EVENT=workflow_dispatch check "so does a manual run" 0 "wire=true" "$wire"

check "a glob this script does not implement is refused" 2 "" $'wire:\n  - \'pg-core/*.rs\'' a
check "a mid-path ** is refused" 2 "" $'wire:\n  - \'pg-*/**\'' a
check "a negation is refused" 2 "" $'wire:\n  - \'!pg-core/**\'' a
check "two filter names are refused" 2 "" $'wire:\n  - \'a\'\napi:\n  - \'b\'' a
check "a filter with no patterns is refused" 2 "" $'wire:' a
check "an empty FILTERS is refused" 2 "" "" a
check "a pattern before the name is refused" 2 "" $'- \'a\'\nwire:' a

# On a pull request with no fixture and no PR to read, the answer is unknown.
code=0
env -u GITHUB_OUTPUT -u CHANGED_FILES -u PR_NUMBER FILTERS="$wire" \
  GITHUB_EVENT_NAME=pull_request "$gate" >/dev/null 2>&1 || code=$?
if [[ $code -eq 2 ]]; then
  pass=$((pass + 1))
  echo "ok   a pull request with no number to read is undetermined"
else
  fail=$((fail + 1))
  echo "FAIL a pull request with no number to read: exit $code (want 2)"
fi

# With GITHUB_OUTPUT set, the answer goes to that file and not to stdout.
printf '%s\n' pg-core/x >"$tmp/changed"
out=$(GITHUB_OUTPUT="$tmp/output" FILTERS="$wire" CHANGED_FILES="$tmp/changed" \
  GITHUB_EVENT_NAME=pull_request "$gate" 2>/dev/null)
if [[ -z $out && $(cat "$tmp/output") == "wire=true" ]]; then
  pass=$((pass + 1))
  echo "ok   the answer is written to GITHUB_OUTPUT when it is set"
else
  fail=$((fail + 1))
  echo "FAIL the answer was not written to GITHUB_OUTPUT (stdout '$out')"
fi

echo
echo "$pass passed, $fail failed"
[[ $fail -eq 0 ]]
