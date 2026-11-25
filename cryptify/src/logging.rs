//! Cryptify's logger and access log, used in place of Rocket's logger.
//!
//! Rocket's logger writes four lines for every request (`GET /health:`,
//! `Matched`, `Outcome`, `Response succeeded.`), none of them with a level. A
//! Kubernetes probe arrives twice every five seconds, so a pod that serves no
//! one still wrote about a hundred lines a minute, and a log viewer could not
//! tell an error from the rest.
//!
//! Here every record is one line of logfmt, and a request is the one line
//! [`AccessLog`] writes:
//!
//! ```text
//! level=info target=cryptify::email msg="Email sent to a@b.c"
//! level=info target=access msg=request method=POST uri=/fileupload/init status=200 duration_ms=2
//! ```
//!
//! A probe that finds the service healthy is not logged at all.
//!
//! A field given to a `log` macro, as in `log::info!(uuid = id; "finalized")`,
//! becomes a field of the line.
//!
//! [`init`] runs on the launch path only. Tests build many Rockets in one
//! process and the logger can be set once per process, so they keep Rocket's.

use std::fmt::Write as _;
use std::io::Write as _;
use std::time::Instant;

use log::kv::{Key, Source, Value, VisitSource};
use log::{Level, LevelFilter, Log, Metadata, Record};
use rocket::config::LogLevel;
use rocket::fairing::{Fairing, Info, Kind};
use rocket::figment::Figment;
use rocket::http::Status;
use rocket::{Data, Request, Response};

/// Modules that narrate each request at info level: Rocket's request loop
/// (the four lines above) and rocket_cors (`Verifying origin`, `Origin has a
/// regex match?`). [`AccessLog`] replaces that narration. Their warnings and
/// errors still come through.
///
/// Rocket's request loop also reports a shutdown at info level, and that goes
/// with it. `Received SIGTERM. Requesting shutdown.` is a warning and stays.
const REQUEST_NARRATORS: [&str; 2] = [ROCKET_REQUEST_LOOP, "rocket_cors"];

/// Rocket's request loop. Among the modules above it is the one whose lines
/// [`restates_the_status`] reads.
const ROCKET_REQUEST_LOOP: &str = "rocket::server";

/// Rocket's startup report: the configuration, the routes, the fairings.
/// Rocket sends part of it at warn so that it survives `critical`; it is
/// information and is labelled as such.
const ROCKET_LAUNCH: &str = "rocket::launch";

/// Rocket ends the target of a continuation line in this.
const CONTINUATION: &str = "::_";

/// The target of the line [`AccessLog`] writes for a request.
const ACCESS: &str = "access";

/// The path of the `health` route. A test holds it to the mounted route.
const HEALTH_PATH: &str = "/health";

struct Logger;

impl Log for Logger {
    fn enabled(&self, metadata: &Metadata<'_>) -> bool {
        metadata.level() <= log::max_level() && !narrates_a_request(metadata)
    }

    fn log(&self, record: &Record<'_>) {
        if !self.enabled(record.metadata()) {
            return;
        }
        let message = record.args().to_string();
        if restates_the_status(record.target(), &message) {
            return;
        }
        // Not `println!`, which panics once stdout is closed.
        let _ = writeln!(
            std::io::stdout().lock(),
            "{}",
            line(
                record.level(),
                record.target(),
                &message,
                record.key_values()
            )
        );
    }

    fn flush(&self) {}
}

/// Installs the logger at the level Rocket's `log_level` asks for. Call it
/// before anything logs: a record written before a logger exists is dropped.
///
/// An unreadable `log_level` falls back to `normal` here. Rocket rejects the
/// same value when it ignites, and that error is then logged.
pub fn init(figment: &Figment) {
    static LOGGER: Logger = Logger;

    let level = figment
        .extract_inner::<LogLevel>(rocket::Config::LOG_LEVEL)
        .unwrap_or(LogLevel::Normal);

    // Rocket only sets the level when its own logger is the one installed,
    // so from here on this is the only place that sets it.
    if log::set_logger(&LOGGER).is_ok() {
        log::set_max_level(max_level(level));
    }
}

/// Rocket's `debug` means trace, which adds the internals of hyper and mio:
/// about ninety lines for a single request. Nothing in cryptify logs at
/// trace, so `debug` stops at debug.
fn max_level(level: LogLevel) -> LevelFilter {
    match level {
        LogLevel::Off => LevelFilter::Off,
        LogLevel::Critical => LevelFilter::Warn,
        LogLevel::Normal => LevelFilter::Info,
        LogLevel::Debug => LevelFilter::Debug,
    }
}

fn narrates_a_request(metadata: &Metadata<'_>) -> bool {
    metadata.level() == Level::Info
        && REQUEST_NARRATORS
            .iter()
            .any(|module| is_within(metadata.target(), module))
}

/// What Rocket's request loop says when no route matched, and when it answers
/// with its built-in error page. The status on the access line says both.
///
/// rocket_cors answers every preflight through that path, turning the 404
/// into a 204. Left in, and now with a level on the line, each upload from a
/// browser would read as an error.
///
/// These two are matched on Rocket 0.5's wording, because the same target
/// also carries `Handler panicked` and `Failed to write response`. If Rocket
/// rewords them they come back; nothing is lost.
fn restates_the_status(target: &str, message: &str) -> bool {
    is_within(target, ROCKET_REQUEST_LOOP)
        && (message.starts_with("No matching routes for ")
            || (message.starts_with("No ")
                && message.ends_with(" catcher registered. Using Rocket default.")))
}

/// Whether `target` is `module` or something inside it. `rocket::serverless`
/// is not inside `rocket::server`.
fn is_within(target: &str, module: &str) -> bool {
    target
        .strip_prefix(module)
        .is_some_and(|rest| rest.is_empty() || rest.starts_with("::"))
}

/// One record as one line of logfmt: the level, where it came from, the
/// message, and then the fields the record carries.
fn line(level: Level, target: &str, message: &str, fields: &dyn Source) -> String {
    let (target, marker) = match target.strip_suffix(CONTINUATION) {
        Some(parent) => (parent, ">> "),
        None => (target, ""),
    };
    let level = if is_within(target, ROCKET_LAUNCH) {
        Level::Info
    } else {
        level
    };

    let mut line = Line(String::new());
    line.field("level", level_name(level));
    line.field("target", target);
    line.field("msg", &format!("{marker}{message}"));
    // `Line` accepts every pair, so the visit cannot fail.
    let _ = fields.visit(&mut line);
    line.0
}

/// Lowercase, which is what a log collector looks for in a `level` field.
fn level_name(level: Level) -> &'static str {
    match level {
        Level::Error => "error",
        Level::Warn => "warn",
        Level::Info => "info",
        Level::Debug => "debug",
        Level::Trace => "trace",
    }
}

/// A line of logfmt being written.
struct Line(String);

impl Line {
    /// Appends `key=value`. A value that a logfmt reader would split, or stop
    /// at, is quoted. That also keeps a message that spans lines on one line,
    /// because a newline in a quoted value is written as `\n`.
    fn field(&mut self, key: &str, value: &str) {
        if !self.0.is_empty() {
            self.0.push(' ');
        }
        // A key cannot be quoted, so what would break it is replaced.
        self.0
            .extend(key.chars().map(|c| if breaks_a_word(c) { '_' } else { c }));
        self.0.push('=');

        if !value.is_empty() && !value.chars().any(breaks_a_word) {
            self.0.push_str(value);
            return;
        }

        self.0.push('"');
        for c in value.chars() {
            match c {
                '"' => self.0.push_str("\\\""),
                '\\' => self.0.push_str("\\\\"),
                '\n' => self.0.push_str("\\n"),
                '\r' => self.0.push_str("\\r"),
                '\t' => self.0.push_str("\\t"),
                c if c.is_control() => {
                    let _ = write!(self.0, "\\u{:04x}", u32::from(c));
                }
                c => self.0.push(c),
            }
        }
        self.0.push('"');
    }
}

impl<'kvs> VisitSource<'kvs> for Line {
    fn visit_pair(&mut self, key: Key<'kvs>, value: Value<'kvs>) -> Result<(), log::kv::Error> {
        self.field(key.as_str(), &value.to_string());
        Ok(())
    }
}

/// Whether `c` ends an unquoted logfmt key or value, or is not allowed in one.
fn breaks_a_word(c: char) -> bool {
    c == ' ' || c == '=' || c == '"' || c.is_control()
}

/// Logs each request as one line: method, URI, status, and the milliseconds
/// until the response was ready to be written.
///
/// Attach it last. Response fairings run in the order they were attached, and
/// rocket_cors turns the 404 of a preflight into a 204 in its own; attached
/// earlier, this would log a status the client never received.
pub struct AccessLog;

struct Started(Instant);

#[rocket::async_trait]
impl Fairing for AccessLog {
    fn info(&self) -> Info {
        Info {
            name: "Access log",
            kind: Kind::Request | Kind::Response,
        }
    }

    async fn on_request(&self, request: &mut Request<'_>, _: &mut Data<'_>) {
        request.local_cache(|| Started(Instant::now()));
    }

    async fn on_response<'r>(&self, request: &'r Request<'_>, response: &mut Response<'r>) {
        let status = response.status();
        if is_healthy_probe(request.uri().path().as_str(), status) {
            return;
        }
        let elapsed = request.local_cache(|| Started(Instant::now())).0.elapsed();

        log::info!(
            target: ACCESS,
            method = request.method().as_str(),
            uri:% = request.uri(),
            status = status.code,
            duration_ms = u64::try_from(elapsed.as_millis()).unwrap_or(u64::MAX);
            "request"
        );
    }
}

/// A probe that found the service healthy: the one request that is not
/// logged. An unhealthy probe is logged like any other request, next to the
/// error `health` itself writes.
fn is_healthy_probe(path: &str, status: Status) -> bool {
    path == HEALTH_PATH && status.class().is_success()
}

#[cfg(test)]
mod tests {
    use super::*;
    use rocket::http::{uri::Origin, Method};

    /// Whether a request for `uri` that got `status` goes unlogged.
    fn unlogged(uri: &'static str, status: Status) -> bool {
        is_healthy_probe(Origin::parse(uri).unwrap().path().as_str(), status)
    }

    fn narrated(level: Level, target: &str) -> bool {
        narrates_a_request(&Metadata::builder().level(level).target(target).build())
    }

    /// A record that carries no fields of its own.
    fn rendered(level: Level, target: &str, message: &str) -> String {
        let no_fields: [(&str, Value<'_>); 0] = [];
        line(level, target, message, &no_fields)
    }

    #[test]
    fn a_healthy_probe_is_not_logged() {
        assert!(unlogged("/health", Status::Ok));
        assert!(unlogged("/health?from=kubelet", Status::Ok));
    }

    #[test]
    fn an_unhealthy_probe_is_logged() {
        assert!(!unlogged("/health", Status::ServiceUnavailable));
    }

    #[test]
    fn any_other_request_is_logged() {
        assert!(!unlogged("/fileupload/init", Status::Ok));
        // A path that only starts like the health route is not the probe.
        assert!(!unlogged("/healthz", Status::Ok));
    }

    /// `HEALTH_PATH` is a second copy of the path in `#[get("/health")]`. If
    /// the route moves and the constant does not, probes are logged again and
    /// nothing else says so.
    #[test]
    fn the_quiet_path_is_the_mounted_health_route() {
        assert!(crate::api_routes()
            .iter()
            .any(|route| route.method == Method::Get && route.uri.path() == HEALTH_PATH));
    }

    #[test]
    fn the_narration_of_a_request_is_dropped() {
        // `GET /health:` and the indented lines under it.
        assert!(narrated(Level::Info, "rocket::server"));
        assert!(narrated(Level::Info, "rocket::server::_"));
        // `Verifying origin: ...`
        assert!(narrated(Level::Info, "rocket_cors::_"));
        assert!(narrated(Level::Info, "rocket_cors::fairing::_"));
    }

    #[test]
    fn a_warning_from_the_request_loop_is_kept() {
        // `Received SIGTERM. Requesting shutdown.`
        assert!(!narrated(Level::Warn, "rocket::server"));
        // `Handler upload_chunk panicked.`
        assert!(!narrated(Level::Error, "rocket::server::_"));
        // `CORS Error: ...`
        assert!(!narrated(Level::Error, "rocket_cors::_"));
    }

    #[test]
    fn what_the_access_line_already_says_is_dropped() {
        assert!(restates_the_status(
            "rocket::server::_",
            "No matching routes for OPTIONS /fileupload/init."
        ));
        assert!(restates_the_status(
            "rocket::server::_",
            "No 404 catcher registered. Using Rocket default."
        ));
    }

    #[test]
    fn a_failure_from_the_request_loop_is_kept() {
        assert!(!restates_the_status(
            "rocket::server::_",
            "Handler upload_chunk panicked."
        ));
        assert!(!restates_the_status(
            "rocket::server::_",
            "Failed to write response: broken pipe."
        ));
        // The same words from cryptify's own code are not Rocket's.
        assert!(!restates_the_status(
            "cryptify",
            "No matching routes for GET /nope."
        ));
    }

    #[test]
    fn info_from_anywhere_else_is_kept() {
        assert!(!narrated(Level::Info, "cryptify::email"));
        assert!(!narrated(Level::Info, ACCESS));
        assert!(!narrated(Level::Info, "rocket::launch"));
        assert!(!narrated(Level::Info, "rocket::shield::shield"));
        // Starts with the same letters, is a different module.
        assert!(!narrated(Level::Info, "rocket::serverless"));
    }

    #[test]
    fn a_record_is_level_target_and_message() {
        assert_eq!(
            rendered(Level::Info, "cryptify::email", "Email sent to a@b.c"),
            r#"level=info target=cryptify::email msg="Email sent to a@b.c""#
        );
        assert_eq!(
            rendered(Level::Error, "cryptify", "failed"),
            "level=error target=cryptify msg=failed"
        );
    }

    #[test]
    fn the_fields_of_a_record_follow_the_message() {
        let fields = [
            ("method", Value::from("POST")),
            ("uri", Value::from("/fileupload/init")),
            ("status", Value::from(200u16)),
            ("duration_ms", Value::from(12u64)),
        ];

        assert_eq!(
            line(Level::Info, ACCESS, "request", &fields),
            "level=info target=access msg=request \
             method=POST uri=/fileupload/init status=200 duration_ms=12"
        );
    }

    #[test]
    fn a_value_a_reader_would_split_is_quoted() {
        // The one route with a query: `=` would start a second pair.
        let fields = [("uri", Value::from("/usage?email=a@b.c"))];
        assert_eq!(
            line(Level::Info, ACCESS, "request", &fields),
            r#"level=info target=access msg=request uri="/usage?email=a@b.c""#
        );

        assert_eq!(
            rendered(Level::Warn, "cryptify", r#"got "x\y""#),
            r#"level=warn target=cryptify msg="got \"x\\y\"""#
        );
        assert_eq!(
            rendered(Level::Info, "cryptify", ""),
            r#"level=info target=cryptify msg="""#
        );
    }

    #[test]
    fn a_message_that_spans_lines_stays_one_line() {
        let line = rendered(
            Level::Debug,
            "rocket::server",
            "Request {\n    method: GET,\n}",
        );

        assert_eq!(
            line,
            r#"level=debug target=rocket::server msg="Request {\n    method: GET,\n}""#
        );
        assert!(!line.contains('\n'));
    }

    #[test]
    fn a_key_cannot_break_the_line() {
        let fields = [("upload id", Value::from(7u8))];

        assert_eq!(
            line(Level::Info, "cryptify", "stored", &fields),
            "level=info target=cryptify msg=stored upload_id=7"
        );
    }

    #[test]
    fn a_continuation_line_is_marked() {
        assert_eq!(
            rendered(
                Level::Warn,
                "rocket::server::_",
                "Remote left: broken pipe."
            ),
            r#"level=warn target=rocket::server msg=">> Remote left: broken pipe.""#
        );
    }

    #[test]
    fn the_launch_report_is_info_even_where_rocket_sends_it_as_a_warning() {
        assert_eq!(
            rendered(Level::Warn, "rocket::launch", "Rocket has launched"),
            r#"level=info target=rocket::launch msg="Rocket has launched""#
        );
        assert_eq!(
            rendered(Level::Warn, "rocket::launch::_", "address: 0.0.0.0"),
            r#"level=info target=rocket::launch msg=">> address: 0.0.0.0""#
        );
    }

    #[test]
    fn debug_stops_short_of_trace() {
        assert_eq!(max_level(LogLevel::Off), LevelFilter::Off);
        assert_eq!(max_level(LogLevel::Critical), LevelFilter::Warn);
        assert_eq!(max_level(LogLevel::Normal), LevelFilter::Info);
        assert_eq!(max_level(LogLevel::Debug), LevelFilter::Debug);
    }
}
