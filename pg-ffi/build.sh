#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")"

echo "Building pg-ffi native library..."
cargo build --release

# Detect platform and copy to .NET runtimes directory
UNAME_S=$(uname -s)
UNAME_M=$(uname -m)

if [ "$UNAME_S" = "Darwin" ]; then
    LIB_EXT="dylib"
    if [ "$UNAME_M" = "arm64" ]; then
        RID="osx-arm64"
    else
        RID="osx-x64"
    fi
elif [ "$UNAME_S" = "Linux" ]; then
    LIB_EXT="so"
    if [ "$UNAME_M" = "aarch64" ]; then
        RID="linux-arm64"
    else
        RID="linux-x64"
    fi
else
    echo "Unsupported platform: $UNAME_S"
    exit 1
fi

RELEASE_DIR="target/release"

# If building as part of workspace, output goes to workspace root target/
if [ -f "../Cargo.toml" ] && grep -q '\[workspace\]' "../Cargo.toml"; then
    RELEASE_DIR="../target/release"
fi

echo "Built libpg_ffi.${LIB_EXT} at ${RELEASE_DIR}/"

# Copy into the .NET SDK next door, where the csproj packs it from and the
# test project copies it next to the test assembly.
DEST="../pg-dotnet/src/runtimes/${RID}/native"
mkdir -p "$DEST"
cp "${RELEASE_DIR}/libpg_ffi.${LIB_EXT}" "$DEST/"
echo "Copied to ${DEST}/"
