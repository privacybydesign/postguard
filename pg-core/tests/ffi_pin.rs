//! Pins the `=` in `pg-ffi`'s requirement on `pg-core` (#256).
//!
//! `E4A.PostGuard` ships the `pg-ffi` native library and is published at
//! `pg-core`'s version. That number is only honest if a `pg-core` release
//! always rebuilds and re-releases `pg-ffi`. With a caret requirement it does
//! not: `"0.6"` is satisfied by every 0.6.x, so a patch release of `pg-core`
//! edits nothing in `pg-ffi/Cargo.toml`, release-plz sees no change to
//! `pg-ffi`, and the newest native library stays one or more `pg-core`
//! releases behind. That is how `pg-ffi-v0.1.3` came to embed 0.6.2 while
//! `main` was at 0.6.10.
//!
//! With `=`, cargo itself refuses to resolve the workspace once the two
//! disagree, so a one-sided bump is red everywhere at once. What cargo cannot
//! see is the requirement being loosened back to a caret; that is the case
//! this test is for. A release-plz release PR that bumps `pg-core` without
//! this line goes red on purpose: fix the line in that PR.
//!
//! Read as text, not parsed TOML, for the reason given in `release_config.rs`.

use std::fs;
use std::path::PathBuf;

fn manifest(krate: &str) -> String {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join(krate)
        .join("Cargo.toml");
    fs::read_to_string(&path)
        .unwrap_or_else(|e| panic!("read {}: {e}", path.display()))
        .replace("\r\n", "\n")
}

/// The quoted value of `needle = "..."` on `line`.
fn quoted_after<'a>(line: &'a str, needle: &str) -> Option<&'a str> {
    let rest = &line[line.find(needle)? + needle.len()..];
    let rest = rest.trim_start().strip_prefix('=')?.trim_start();
    let rest = rest.strip_prefix('"')?;
    Some(&rest[..rest.find('"')?])
}

#[test]
fn pg_ffi_requires_exactly_this_pg_core() {
    let pg_core = manifest("pg-core");
    let version = pg_core
        .lines()
        .skip_while(|line| line.trim() != "[package]")
        .skip(1)
        .take_while(|line| !line.trim_start().starts_with('['))
        .find(|line| line.trim_start().starts_with("version"))
        .and_then(|line| quoted_after(line, "version"))
        .expect("pg-core/Cargo.toml has no `version` in its [package] table");

    let pg_ffi = manifest("pg-ffi");
    let dependency = pg_ffi
        .lines()
        .map(str::trim)
        .find(|line| line.starts_with("pg-core") && line.contains('='))
        .expect("pg-ffi/Cargo.toml no longer depends on pg-core");
    let requirement = quoted_after(dependency, "version")
        .expect("pg-ffi's pg-core dependency has no `version` requirement");

    assert_eq!(
        requirement,
        format!("={version}"),
        "pg-ffi requires pg-core {requirement:?}, and pg-core is {version}. The requirement \
         must be exactly `={version}`: E4A.PostGuard is published at pg-core's version with \
         the pg-ffi built from the same commit, and any looser requirement lets a pg-core \
         release go out without a pg-ffi rebuild (#256)"
    );
}
