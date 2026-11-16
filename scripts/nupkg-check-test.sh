#!/usr/bin/env bash
#
# Regression tests for scripts/nupkg-check.sh, covering its exit-code contract:
# 1 means the package is wrong, 2 means the script could not find out.
#
# Entirely offline: every fixture is a zip built in a temp dir. No dotnet.
#
# Usage:
#   scripts/nupkg-check-test.sh
#
set -euo pipefail

root=$(git rev-parse --show-toplevel)
check="$root/scripts/nupkg-check.sh"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

libs=(
  runtimes/linux-x64/native/libpg_ffi.so
  runtimes/linux-arm64/native/libpg_ffi.so
  runtimes/osx-arm64/native/libpg_ffi.dylib
  runtimes/osx-x64/native/libpg_ffi.dylib
  runtimes/win-x64/native/pg_ffi.dll
)

# make_package <directory> <version> <lib>...
make_package() {
  local dir=$1 version=$2
  shift 2
  mkdir -p "$dir/stage"
  : >"$dir/stage/E4A.PostGuard.nuspec"
  local lib
  for lib in "$@"; do
    mkdir -p "$dir/stage/$(dirname "$lib")"
    : >"$dir/stage/$lib"
  done
  (cd "$dir/stage" && zip -qr "../E4A.PostGuard.$version.nupkg" .)
  rm -rf "$dir/stage"
}

pass=0
fail=0

expect() { # <name> <expected exit> <args...>
  local name=$1 want=$2 got=0
  shift 2
  "$check" "$@" >"$tmp/stdout" 2>"$tmp/stderr" || got=$?
  if [[ "$got" == "$want" ]]; then
    pass=$((pass + 1))
    echo "ok   $name"
  else
    fail=$((fail + 1))
    echo "FAIL $name: exit $got, want $want"
    sed 's/^/     /' "$tmp/stderr"
  fi
}

make_package "$tmp/complete" 0.6.10 "${libs[@]}"
expect "a package with all five libraries passes" 0 "$tmp/complete"
expect "  also at the version it was asked for" 0 "$tmp/complete" 0.6.10
expect "  and fails at any other version" 1 "$tmp/complete" 0.6.11

# Each platform on its own: a check that only looked for one, or that matched
# on the file name, would let the two Linux or the two macOS entries cover for
# each other.
for missing in "${libs[@]}"; do
  rid=${missing#runtimes/}
  rid=${rid%%/*}
  kept=()
  for lib in "${libs[@]}"; do
    [[ "$lib" == "$missing" ]] || kept+=("$lib")
  done
  make_package "$tmp/no-$rid" 0.6.10 "${kept[@]}"
  expect "a package without $rid fails" 1 "$tmp/no-$rid"
done

mkdir "$tmp/empty"
expect "a directory with no package is undetermined" 2 "$tmp/empty"

make_package "$tmp/two" 0.6.10 "${libs[@]}"
make_package "$tmp/two" 0.6.11 "${libs[@]}"
expect "a directory with two packages is undetermined" 2 "$tmp/two"

mkdir "$tmp/garbage"
echo "not a zip" >"$tmp/garbage/E4A.PostGuard.0.6.10.nupkg"
expect "an unreadable package is undetermined" 2 "$tmp/garbage"

echo
echo "$pass passed, $fail failed"
[[ "$fail" == 0 ]]
