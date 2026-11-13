#!/usr/bin/env bash
#
# Checks a packed E4A.PostGuard before it is uploaded or published.
#
# `dotnet pack` globs pg-dotnet/src/runtimes/<rid>/native/** and packs whatever
# is there. A platform whose library is missing packs without a warning, and
# the package then fails in the consumer's process with DllNotFoundException
# on that platform only. So the package is opened and held to the five
# libraries delivery.yml's build-ffi matrix produces.
#
# With a second argument, the package must also be at exactly that version:
# publish-dotnet passes the pg-core version release-plz just released.
#
# Exit codes: 0 fine; 1 the package is wrong; 2 could not find out (no
# package, or more than one, in the directory).
#
# Usage:
#   scripts/nupkg-check.sh <directory> [<version>]
#
set -euo pipefail

dir=${1:?usage: nupkg-check.sh <directory> [<version>]}
want_version=${2:-}

shopt -s nullglob
packages=("$dir"/E4A.PostGuard.*.nupkg)
if [[ ${#packages[@]} -ne 1 ]]; then
  echo "::error::expected exactly one E4A.PostGuard.*.nupkg in $dir, found ${#packages[@]}" >&2
  exit 2
fi
package=${packages[0]}
entries=$(unzip -Z1 "$package") || {
  echo "::error::$package is not a readable archive" >&2
  exit 2
}

bad=0
for lib in \
  runtimes/linux-x64/native/libpg_ffi.so \
  runtimes/linux-arm64/native/libpg_ffi.so \
  runtimes/osx-arm64/native/libpg_ffi.dylib \
  runtimes/osx-x64/native/libpg_ffi.dylib \
  runtimes/win-x64/native/pg_ffi.dll; do
  if ! grep -qxF "$lib" <<<"$entries"; then
    echo "::error::$(basename "$package") does not contain $lib" >&2
    bad=1
  fi
done

version=$(basename "$package" .nupkg)
version=${version#E4A.PostGuard.}
if [[ -n "$want_version" && "$version" != "$want_version" ]]; then
  echo "::error::the package is at $version, and the pg-core release is $want_version" >&2
  bad=1
fi

if [[ "$bad" == 0 ]]; then
  echo "$(basename "$package"): all five native libraries present, version $version"
fi
exit "$bad"
