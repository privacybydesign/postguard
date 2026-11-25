//! Cryptify's logger and access log, used in place of Rocket's logger.
//!
//! Rocket's logger writes four lines for every request (`GET /health:`,
//! `Matched`, `Outcome`, `Response succeeded.`), none of them with a level. A
//! Kubernetes probe arrives twice every five seconds, so a pod that serves no
//! one still wrote about a hundred lines a minute, and a log viewer could not
//! tell an error from the rest.
//!
//! Here every record is one line that starts with its level, and a request is
//! one line from [`AccessLog`]. A probe that finds the service healthy is not
//! logged at all.
//!
//! [`init`] runs on the launch path only. Tests build many Rockets in one
//! process and the logger can be set once per process, so they keep Rocket's.

use std::io::Write;
use std::time::{Duration, Instant};

use log::{Level, LevelFilter, Log, Metadata, Record};
use rocket::config::LogLevel;
use rocket::fairing::{Fairing, Info, Kind};
use rocket::figment::Figment;
use rocket::http::{uri::Origin, Method, Status};
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
            line(record.level(), record.target(), &message)
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
/// into a 204. Left in, and now with a level in front, each upload from a
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

/// One record as one line: the level, where it came from, the message.
fn line(level: Level, target: &str, message: &str) -> String {
    let (target, marker) = match target.strip_suffix(CONTINUATION) {
        Some(parent) => (parent, ">> "),
        None => (target, ""),
    };
    let level = if is_within(target, ROCKET_LAUNCH) {
        Level::Info
    } else {
        level
    };
    // A message that spans lines would reach a log collector as that many
    // separate entries, only the first of them carrying the level.
    let message = message.replace('\n', "\\n");

    format!("{level:<5} {target}: {marker}{message}")
}

/// Logs each request as one line: method, URI, status, and the time until the
/// response was ready to be written.
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
        let elapsed = request.local_cache(|| Started(Instant::now())).0.elapsed();

        if let Some(line) = access_line(request.method(), request.uri(), response.status(), elapsed)
        {
            log::info!(target: ACCESS, "{line}");
        }
    }
}

/// The line for one request, or `None` for a probe that found the service
/// healthy. An unhealthy probe is logged like any other request, next to the
/// error `health` itself writes.
fn access_line(
    method: Method,
    uri: &Origin<'_>,
    status: Status,
    elapsed: Duration,
) -> Option<String> {
    if uri.path() == HEALTH_PATH && status.class().is_success() {
        return None;
    }

    Some(format!(
        "{method} {uri} {} {}ms",
        status.code,
        elapsed.as_millis()
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn uri(s: &'static str) -> Origin<'static> {
        Origin::parse(s).unwrap()
    }

    fn narrated(level: Level, target: &str) -> bool {
        narrates_a_request(&Metadata::builder().level(level).target(target).build())
    }

    #[test]
    fn a_healthy_probe_is_not_logged() {
        let took = Duration::from_millis(1);

        assert_eq!(
            access_line(Method::Get, &uri("/health"), Status::Ok, took),
            None
        );
        assert_eq!(
            access_line(Method::Get, &uri("/health?from=kubelet"), Status::Ok, took),
            None
        );
    }

    #[test]
    fn an_unhealthy_probe_is_logged() {
        assert_eq!(
            access_line(
                Method::Get,
                &uri("/health"),
                Status::ServiceUnavailable,
                Duration::from_millis(7)
            )
            .as_deref(),
            Some("GET /health 503 7ms")
        );
    }

    #[test]
    fn any_other_request_is_one_line() {
        assert_eq!(
            access_line(
                Method::Post,
                &uri("/fileupload/init"),
                Status::Ok,
                Duration::from_millis(12)
            )
            .as_deref(),
            Some("POST /fileupload/init 200 12ms")
        );
        // A path that only starts like the health route is not the probe.
        assert!(access_line(
            Method::Get,
            &uri("/healthz"),
            Status::NotFound,
            Duration::ZERO
        )
        .is_some());
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
        // `No matching routes for GET /nope.`
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
    fn a_line_starts_with_its_level() {
        assert_eq!(
            line(Level::Info, "cryptify::email", "Email sent to a@b.c"),
            "INFO  cryptify::email: Email sent to a@b.c"
        );
        assert_eq!(
            line(Level::Error, "cryptify", "health: probe failed"),
            "ERROR cryptify: health: probe failed"
        );
    }

    #[test]
    fn a_continuation_line_is_marked() {
        assert_eq!(
            line(
                Level::Warn,
                "rocket::server::_",
                "No 404 catcher registered."
            ),
            "WARN  rocket::server: >> No 404 catcher registered."
        );
    }

    #[test]
    fn the_launch_report_is_info_even_where_rocket_sends_it_as_a_warning() {
        assert_eq!(
            line(Level::Warn, "rocket::launch", "Rocket has launched"),
            "INFO  rocket::launch: Rocket has launched"
        );
        assert_eq!(
            line(Level::Warn, "rocket::launch::_", "address: 0.0.0.0"),
            "INFO  rocket::launch: >> address: 0.0.0.0"
        );
    }

    #[test]
    fn a_message_that_spans_lines_stays_one_line() {
        assert_eq!(
            line(
                Level::Debug,
                "rocket::server",
                "Request {\n    method: GET,\n}"
            ),
            "DEBUG rocket::server: Request {\\n    method: GET,\\n}"
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
