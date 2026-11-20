//! How long an upload's bytes stay on disk, and the task that removes them.
//!
//! The file in `data_dir` is the only thing that outlives an upload session.
//! The session itself (the in-memory entry and its `upload_sessions` row) is
//! evicted after the idle window, an hour by default, long before the
//! [`UPLOAD_RETENTION_SECS`] a finalized upload has to stay downloadable. So
//! nothing in the store can say when a finalized upload expires, and until
//! this module existed nothing did: the file stayed, and with it the download
//! link, for as long as the volume lasted.
//!
//! The filesystem is therefore the source of truth. A file's age is read from
//! its own metadata ([`upload_born`]), the sweep removes what is past
//! retention ([`sweep`]), and the download route applies the same test
//! ([`is_expired`]) so that a file the sweep has not reached yet, or could not
//! remove, is already unavailable.
//!
//! Only files named like an upload are ever candidates ([`is_upload_name`]).
//! Anything else in `data_dir` is not this module's to delete.

use std::fs::Metadata;
use std::path::Path;
use std::time::{Duration, SystemTime};

/// How long an upload stays available, counted from `upload_init`. This is
/// the expiry date the notification email states to the recipient.
pub const UPLOAD_RETENTION_SECS: u64 = 1_209_600;

/// How often the sweep runs. Retention is counted in days, so a file outlives
/// its expiry on disk by at most this long, and the download route does not
/// serve it in the meantime.
pub const SWEEP_INTERVAL: Duration = Duration::from_secs(60 * 60);

/// Whether `name` is a name `upload_init` gives a file: a hyphenated,
/// lower-case UUID and nothing else.
///
/// Both the download route and the sweep go through this, so the two agree on
/// what an upload is.
pub fn is_upload_name(name: &str) -> bool {
    match uuid::Uuid::try_parse(name) {
        // `try_parse` also accepts the simple, braced and URN spellings and
        // upper case. Only the spelling `upload_init` writes is a file name.
        Ok(uuid) => uuid.hyphenated().to_string() == name,
        Err(_) => false,
    }
}

/// When the upload this file belongs to began, as far as the file can say.
///
/// The earlier of the creation and modification times. Creation time is the
/// exact answer, because `upload_init` creates the file, but it is reset when
/// a volume is copied to a new one and it is not available on every
/// filesystem. Modification time survives a copy that preserves timestamps and
/// is always available, but it moves with every chunk. Taking the earlier of
/// the two is right in both cases and can only err towards expiring late by
/// the duration of the upload. `None` when the metadata carries neither.
pub fn upload_born(meta: &Metadata) -> Option<SystemTime> {
    match (meta.created().ok(), meta.modified().ok()) {
        (Some(created), Some(modified)) => Some(created.min(modified)),
        (created, modified) => created.or(modified),
    }
}

/// Whether a file with this metadata is past retention at `now`.
///
/// A file whose age cannot be read is not expired: the sweep must not delete
/// on a guess, and the two callers agree by sharing this function.
pub fn is_expired(meta: &Metadata, now: SystemTime) -> bool {
    let Some(born) = upload_born(meta) else {
        return false;
    };
    match now.duration_since(born) {
        Ok(age) => age >= Duration::from_secs(UPLOAD_RETENTION_SECS),
        // Born in the future: a clock that stepped back. Not expired.
        Err(_) => false,
    }
}

/// What one pass over `data_dir` did.
#[derive(Debug, Default, PartialEq, Eq)]
pub struct SweepReport {
    /// Expired uploads removed.
    pub removed: usize,
    /// Expired uploads that could not be removed. They stay unavailable for
    /// download and are tried again on the next pass.
    pub failed: usize,
}

/// Remove every upload in `data_dir` that is past retention at `now`.
///
/// Flat, like the directory: subdirectories are not entered, and a name that
/// is not an upload name is not looked at. An error reading the directory
/// itself is returned; an error on one file is counted and logged, and the
/// pass carries on.
pub fn sweep(data_dir: &Path, now: SystemTime) -> std::io::Result<SweepReport> {
    let mut report = SweepReport::default();
    for entry in std::fs::read_dir(data_dir)? {
        let Ok(entry) = entry else { continue };
        let name = entry.file_name();
        let Some(name) = name.to_str() else { continue };
        if !is_upload_name(name) {
            continue;
        }
        // `symlink_metadata`, so a link is judged as a link and never as
        // whatever it points at.
        let Ok(meta) = std::fs::symlink_metadata(entry.path()) else {
            continue;
        };
        if !meta.is_file() || !is_expired(&meta, now) {
            continue;
        }
        match std::fs::remove_file(entry.path()) {
            Ok(()) => report.removed += 1,
            // Gone between the listing and the unlink: the outcome wanted.
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => {
                report.failed += 1;
                // The name stays out of the log: it is the address the upload
                // is downloaded from.
                log::warn!("retention: could not remove an expired upload: {}", e);
            }
        }
    }
    Ok(report)
}

/// Sweep `data_dir` now and then every `interval`, for the life of the
/// process.
///
/// The first pass runs at startup on purpose: it is what clears the backlog a
/// deployment built up while nothing removed expired uploads.
pub async fn sweeper(data_dir: std::path::PathBuf, interval: Duration) {
    loop {
        let dir = data_dir.clone();
        // The walk and the unlinks are blocking calls, and a backlog can be
        // hundreds of files.
        let pass =
            rocket::tokio::task::spawn_blocking(move || sweep(&dir, SystemTime::now())).await;
        match pass {
            Ok(Ok(report)) if report.removed > 0 || report.failed > 0 => log::info!(
                "retention: removed {} expired upload(s), {} could not be removed",
                report.removed,
                report.failed
            ),
            Ok(Ok(_)) => {}
            Ok(Err(e)) => log::warn!("retention: could not sweep {:?}: {}", data_dir, e),
            Err(e) => log::error!("retention: sweep task failed: {}", e),
        }
        rocket::tokio::time::sleep(interval).await;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const DAY: Duration = Duration::from_secs(24 * 60 * 60);

    fn temp_dir() -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "cryptify-retention-{}",
            uuid::Uuid::new_v4().hyphenated()
        ));
        std::fs::create_dir_all(&dir).expect("create temp dir");
        dir
    }

    fn upload_name() -> String {
        uuid::Uuid::new_v4().hyphenated().to_string()
    }

    /// Write `name` in `dir` and date it `age` before now.
    fn write_aged(dir: &Path, name: &str, age: Duration) {
        let path = dir.join(name);
        std::fs::write(&path, b"payload").expect("write file");
        let file = std::fs::File::options()
            .write(true)
            .open(&path)
            .expect("reopen file");
        file.set_modified(SystemTime::now() - age)
            .expect("set modification time");
    }

    #[test]
    fn only_the_spelling_upload_init_writes_is_an_upload_name() {
        let uuid = uuid::Uuid::new_v4();
        assert!(is_upload_name(&uuid.hyphenated().to_string()));

        assert!(!is_upload_name(&uuid.simple().to_string()));
        assert!(!is_upload_name(&uuid.braced().to_string()));
        assert!(!is_upload_name(&uuid.urn().to_string()));
        assert!(!is_upload_name(
            &uuid.hyphenated().to_string().to_uppercase()
        ));
        assert!(!is_upload_name(""));
        assert!(!is_upload_name("file1"));
        assert!(!is_upload_name("abc-123"));
        assert!(!is_upload_name(".."));
    }

    #[test]
    fn a_file_expires_at_retention_and_not_before() {
        let dir = temp_dir();
        let name = upload_name();
        write_aged(&dir, &name, 13 * DAY);
        let meta = std::fs::metadata(dir.join(&name)).unwrap();

        let now = SystemTime::now();
        assert!(!is_expired(&meta, now), "13 days old is inside retention");
        assert!(is_expired(&meta, now + DAY), "14 days old is past it");

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_file_dated_in_the_future_is_not_expired() {
        let dir = temp_dir();
        let name = upload_name();
        std::fs::write(dir.join(&name), b"payload").unwrap();
        let meta = std::fs::metadata(dir.join(&name)).unwrap();

        assert!(!is_expired(&meta, SystemTime::now() - 30 * DAY));

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_sweep_removes_expired_uploads_and_nothing_else() {
        let dir = temp_dir();
        let expired = upload_name();
        let fresh = upload_name();
        write_aged(&dir, &expired, 15 * DAY);
        write_aged(&dir, &fresh, DAY);
        // Just as old, and not uploads.
        write_aged(&dir, "notes.txt", 400 * DAY);
        write_aged(&dir, "stray", 400 * DAY);
        // A directory with an upload's name is not an upload.
        let dir_named_like_upload = upload_name();
        std::fs::create_dir(dir.join(&dir_named_like_upload)).unwrap();

        let report = sweep(&dir, SystemTime::now()).expect("sweep");

        assert_eq!(
            report,
            SweepReport {
                removed: 1,
                failed: 0
            }
        );
        assert!(!dir.join(&expired).exists(), "the expired upload is gone");
        assert!(
            dir.join(&fresh).exists(),
            "an upload inside retention stays"
        );
        assert!(
            dir.join("notes.txt").exists(),
            "a file that is not an upload stays"
        );
        assert!(dir.join("stray").exists(), "and so does any other");
        assert!(dir.join(&dir_named_like_upload).is_dir());

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_sweep_reports_a_missing_directory_instead_of_an_empty_pass() {
        let dir = temp_dir();
        std::fs::remove_dir_all(&dir).unwrap();

        assert!(sweep(&dir, SystemTime::now()).is_err());
    }

    #[rocket::async_test]
    async fn the_sweeper_clears_a_backlog_on_its_first_pass() {
        let dir = temp_dir();
        let expired = upload_name();
        write_aged(&dir, &expired, 60 * DAY);

        let task = rocket::tokio::spawn(sweeper(dir.clone(), Duration::from_secs(3600)));
        for _ in 0..200 {
            if !dir.join(&expired).exists() {
                break;
            }
            rocket::tokio::time::sleep(Duration::from_millis(10)).await;
        }
        task.abort();

        assert!(
            !dir.join(&expired).exists(),
            "startup is when the backlog goes"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }
}
