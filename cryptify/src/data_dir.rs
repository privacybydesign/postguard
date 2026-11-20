//! Whether the upload directory can do what an upload needs of it.
//!
//! Every upload starts with `upload_init` creating a file in `data_dir`. When
//! that cannot work — the directory is gone, the volume is read-only or full,
//! the ownership drifted — every upload fails, and nothing else about the
//! process shows it: the routes are mounted, the database handle is open, the
//! process is up. On 2026-09-01 production ran like that for five days behind
//! a `/health` that answered `OK` and a storage gauge that read 0.
//!
//! [`probe`] performs the same operation `upload_init` does. `/health` answers
//! from it and the storage sampler publishes it as a gauge, so a probe and a
//! dashboard ask the same question the upload path does.

use std::path::Path;

/// The file the probe creates and removes. Not an upload name, so the
/// download route does not serve it and the retention sweep does not look at
/// it. One fixed name rather than one per call, so a process killed between
/// the two steps leaves one stray file behind and not a growing pile.
const PROBE_FILE: &str = ".cryptify-probe";

/// Create and remove a file in `data_dir`, as `upload_init` would create one.
///
/// An `Err` means an upload started now would fail the same way.
pub fn probe(data_dir: &Path) -> std::io::Result<()> {
    let path = data_dir.join(PROBE_FILE);
    std::fs::File::create(&path)?;
    match std::fs::remove_file(&path) {
        Ok(()) => Ok(()),
        // Two probes can overlap on the one name: `/health` and the sampler
        // run independently. The other one removed it, which is fine.
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(e),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir() -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "cryptify-probe-{}",
            uuid::Uuid::new_v4().hyphenated()
        ));
        std::fs::create_dir_all(&dir).expect("create temp dir");
        dir
    }

    #[test]
    fn a_usable_directory_passes_and_is_left_empty() {
        let dir = temp_dir();

        probe(&dir).expect("a fresh temp dir takes a file");

        assert_eq!(std::fs::read_dir(&dir).unwrap().count(), 0);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_missing_directory_fails() {
        let dir = temp_dir();
        std::fs::remove_dir_all(&dir).unwrap();

        let err = probe(&dir).expect_err("there is nowhere to create the file");
        assert_eq!(err.kind(), std::io::ErrorKind::NotFound);
    }

    #[cfg(unix)]
    #[test]
    fn a_read_only_directory_fails() {
        use std::os::unix::fs::PermissionsExt;

        let dir = temp_dir();
        std::fs::set_permissions(&dir, std::fs::Permissions::from_mode(0o555)).unwrap();
        // Root ignores directory permissions, and CI containers may run as
        // root: only assert when the restriction is real.
        let restricted = std::fs::File::create(dir.join("control")).is_err();

        if restricted {
            assert!(probe(&dir).is_err());
        }

        std::fs::set_permissions(&dir, std::fs::Permissions::from_mode(0o755)).unwrap();
        let _ = std::fs::remove_dir_all(&dir);
    }
}
