//! Pins the shape of `.github/dependabot.yml` (#448, graduated from #254).
//!
//! Dependabot is hosted, so nothing in this tree runs it; the only backstop
//! against a well-meaning edit widening or dropping a rule is this test. It
//! took over from the test on `renovate.json` when the repository moved to the
//! privacybydesign organization, which runs Dependabot. What is pinned is the
//! rule this repo owns: the wire-compat gate's published readers, in
//! `pg-compat/Cargo.toml` and `pg-compat-js/package.json`, are not a bot's to
//! bump (`pg-compat/tests/support_window.rs`'s
//! `no_two_pinned_readers_share_a_minor_line` is the other side of that gate).
//!
//! The old test also asserted that no rule set `automerge: true`. Dependabot
//! has no such setting: it opens pull requests and the rulesets decide what
//! merges, so there is nothing left for that assertion to read.
//!
//! No YAML parser: `pg-core` has none among its dependencies and this is not a
//! reason to add one. The file is split on its `- package-ecosystem:` entries
//! and each entry is read as text, which is enough for "the entry for this
//! directory names that dependency under `ignore:`".
//!
//! ```text
//! cargo test --manifest-path pg-core/Cargo.toml --test dependabot_config
//! ```

use std::fs;
use std::path::PathBuf;

fn repo_root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("..")
}

fn config_path() -> PathBuf {
    repo_root().join(".github/dependabot.yml")
}

/// One `updates:` entry: its ecosystem, the directories it covers and the
/// dependency names it ignores.
#[derive(Debug)]
struct Entry {
    ecosystem: String,
    directories: Vec<String>,
    ignored: Vec<String>,
}

fn unquote(value: &str) -> String {
    value.trim().trim_matches(['"', '\'']).to_owned()
}

/// Every entry under `updates:`, comment lines dropped first so the header's
/// prose cannot be read as configuration.
fn entries() -> Vec<Entry> {
    let path = config_path();
    let text = fs::read_to_string(&path).unwrap_or_else(|e| panic!("read {}: {e}", path.display()));
    let lines: Vec<&str> = text
        .lines()
        .filter(|line| !line.trim_start().starts_with('#'))
        .collect();

    let mut entries: Vec<Entry> = Vec::new();
    for line in lines {
        let trimmed = line.trim();
        if let Some(rest) = trimmed.strip_prefix("- package-ecosystem:") {
            let ecosystem = rest.split('#').next().unwrap_or_default();
            entries.push(Entry {
                ecosystem: unquote(ecosystem),
                directories: Vec::new(),
                ignored: Vec::new(),
            });
            continue;
        }
        let Some(entry) = entries.last_mut() else {
            continue;
        };
        if let Some(rest) = trimmed.strip_prefix("directory:") {
            entry.directories.push(unquote(rest));
        } else if let Some(rest) = trimmed.strip_prefix("directories:") {
            let list = rest.trim().trim_start_matches('[').trim_end_matches(']');
            entry.directories.extend(list.split(',').map(unquote));
        } else if let Some(rest) = trimmed.strip_prefix("- dependency-name:") {
            entry.ignored.push(unquote(rest));
        }
    }

    assert!(
        !entries.is_empty(),
        "{} has no `- package-ecosystem:` entry this test can read; either the file is empty or \
         its layout changed and this parser has to follow it",
        path.display()
    );
    entries
}

/// The single entry of `ecosystem` that covers `directory`.
fn entry_for(ecosystem: &str, directory: &str) -> Entry {
    let mut found: Vec<Entry> = entries()
        .into_iter()
        .filter(|e| e.ecosystem == ecosystem && e.directories.iter().any(|d| d == directory))
        .collect();
    assert_eq!(
        found.len(),
        1,
        "expected exactly one `{ecosystem}` entry covering `{directory}` in {}, found {}",
        config_path().display(),
        found.len()
    );
    found.remove(0)
}

/// `pg-compat`'s `=`-exact pins on published `pg-core` releases are the
/// wire-compat gate's readers, and a routine bump there would silently move
/// what HEAD is checked against.
#[test]
fn pg_compat_leaves_its_pg_core_pins_alone() {
    let entry = entry_for("cargo", "/pg-compat");

    for name in ["pg-core", "pg-core-*"] {
        assert!(
            entry.ignored.iter().any(|i| i == name),
            "the cargo entry for /pg-compat in {} no longer ignores `{name}` -- without it a \
             routine bump can move the wire-compat gate's published readers out from under \
             `no_two_pinned_readers_share_a_minor_line`",
            config_path().display(),
        );
    }
    assert_eq!(
        entry.directories,
        ["/pg-compat"],
        "the cargo entry that ignores pg-core covers more than /pg-compat, so the ignore now \
         reaches crates it was never meant for",
    );
}

/// The same rule for the JavaScript readers: `pg-compat-js/package.json` pins
/// published `@e4a/pg-wasm` and `@e4a/pg-js` releases under aliases.
#[test]
fn pg_compat_js_leaves_its_reader_pins_alone() {
    let entry = entry_for("npm", "/pg-compat-js");

    for name in ["@e4a/*", "pg-js-*", "pg-wasm-*"] {
        assert!(
            entry.ignored.iter().any(|i| i == name),
            "the npm entry for /pg-compat-js in {} no longer ignores `{name}` -- without it a \
             routine bump can move the published readers the JavaScript half of the wire-compat \
             gate opens HEAD's bytes with",
            config_path().display(),
        );
    }
}

/// A directory that does not exist is an entry that updates nothing, and
/// Dependabot reports that on a page nobody reads.
#[test]
fn every_directory_exists() {
    for entry in entries() {
        for directory in &entry.directories {
            // A trailing `/*` is Dependabot's glob for "each subdirectory".
            let literal = directory.trim_end_matches("/*").trim_start_matches('/');
            let path = repo_root().join(literal);
            assert!(
                path.is_dir(),
                "the `{}` entry in {} names `{directory}`, and {} is not a directory",
                entry.ecosystem,
                config_path().display(),
                path.display(),
            );
        }
    }
}
