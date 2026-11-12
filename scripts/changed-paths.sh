#!/usr/bin/env bash
#
# Did this pull request touch any of a job's paths?
#
# The path-filtered jobs in `.github/workflows/build.yml` used
# `dorny/paths-filter` for this. The privacybydesign organization only runs
# GitHub-owned, verified and allowlisted actions, and a workflow that names any
# other action does not start at all, so the question is answered here instead,
# from the same source that action read on a pull request: the list of files
# the API reports for it.
#
# Deliberately smaller than what it replaces. A filter is one named list, and a
# pattern is either a path (`Cargo.lock`) or a directory followed by `/**`
# (`pg-core/**`). Every filter in build.yml is one of those two shapes. Any
# other glob character is refused rather than half-supported: a pattern this
# script matched differently from how its author read it would skip a gate
# while reporting green.
#
# On every event other than `pull_request` the answer is `true`. There is no
# base to compare against on a manual run, and on a push to `main` the gate is
# cheap next to a wrong "nothing changed".
#
# Usage:
#   scripts/changed-paths.sh
#
# Environment:
#   FILTERS            the filter, as a block of the form
#                        name:
#                          - 'path'
#                          - 'dir/**'
#                      Blank lines and `#` comment lines are ignored.
#   GITHUB_EVENT_NAME  set by Actions.
#   PR_NUMBER          the pull request to read. Required on `pull_request`.
#   GH_REPO            owner/name. Required on `pull_request`.
#   GH_TOKEN           read by `gh`. Required on `pull_request`.
#   CHANGED_FILES      optional path to a file listing changed paths, one per
#                      line, read instead of the API. For the test script.
#   GITHUB_OUTPUT      where `<name>=true|false` is appended. Default: stdout.
#
# Exit codes, which must not be conflated:
#
#   0  determined; `<name>=true` or `<name>=false` was written
#   2  undetermined: the filter could not be parsed, or the changed files could
#      not be read. NOT "nothing changed". A job that treated this as `false`
#      would skip its gate on an API hiccup.
#
# scripts/changed-paths-test.sh pins this mapping.
#
set -euo pipefail

die() {
  echo "changed-paths: $*" >&2
  exit 2
}

name=
patterns=()
while IFS= read -r line; do
  line=${line#"${line%%[![:space:]]*}"}
  line=${line%"${line##*[![:space:]]}"}
  [[ -z $line || $line == \#* ]] && continue
  if [[ $line == "- "* ]]; then
    [[ -n $name ]] || die "a pattern appears before the filter's name: $line"
    pattern=${line#- }
    pattern=${pattern#\'}
    pattern=${pattern%\'}
    stem=${pattern%/\*\*}
    [[ -n $stem && $stem != *[\*\?\[\]\{\}\!]* ]] ||
      die "unsupported pattern '$pattern': only 'path' and 'dir/**' are understood"
    patterns+=("$pattern")
  elif [[ $line =~ ^([A-Za-z_][A-Za-z0-9_-]*):$ ]]; then
    [[ -z $name ]] || die "more than one filter name ('$name', then '${BASH_REMATCH[1]}')"
    name=${BASH_REMATCH[1]}
  else
    die "cannot parse this line of FILTERS: $line"
  fi
done <<<"${FILTERS:-}"

[[ -n $name ]] || die "FILTERS names no filter"
[[ ${#patterns[@]} -gt 0 ]] || die "filter '$name' lists no patterns"

emit() {
  echo "changed-paths: $name=$1 ($2)" >&2
  if [[ -n ${GITHUB_OUTPUT:-} ]]; then
    echo "$name=$1" >>"$GITHUB_OUTPUT"
  else
    echo "$name=$1"
  fi
  exit 0
}

if [[ -n ${CHANGED_FILES:-} ]]; then
  [[ -r $CHANGED_FILES ]] || die "cannot read $CHANGED_FILES"
  changed=$(cat "$CHANGED_FILES")
elif [[ ${GITHUB_EVENT_NAME:-} != pull_request ]]; then
  emit true "event '${GITHUB_EVENT_NAME:-unset}' has no pull request to compare, so the job runs"
else
  [[ -n ${PR_NUMBER:-} && -n ${GH_REPO:-} ]] || die "PR_NUMBER and GH_REPO must be set on pull_request"
  # A rename is reported under its new name; the old one counts as touched too.
  changed=$(gh api --paginate "repos/$GH_REPO/pulls/$PR_NUMBER/files" \
    --jq '.[] | .filename, (.previous_filename // empty)') ||
    die "could not list the files of $GH_REPO#$PR_NUMBER"
fi

while IFS= read -r file; do
  [[ -n $file ]] || continue
  for pattern in "${patterns[@]}"; do
    if [[ $pattern == */\*\* ]]; then
      [[ $file == "${pattern%\*\*}"* ]] && emit true "$file matches '$pattern'"
    else
      [[ $file == "$pattern" ]] && emit true "$file matches '$pattern'"
    fi
  done
done <<<"$changed"

emit false "no changed file matches the filter"
