use crate::Error;
use actix_web::http::header::AUTHORIZATION;
use actix_web::web::Data;
use actix_web::HttpRequest;
use prometheus::{Encoder, TextEncoder};
use subtle::ConstantTimeEq;

/// The bearer token `/metrics` requires, when the deployment configured one
/// (`--metrics-token` / `PKG_METRICS_TOKEN`).
///
/// `/metrics` is registered on the root app, outside the `/v2` scope, so
/// neither the rate limiter nor the `Auth` middleware ever sees it, and the
/// ingress forwards every path. Without a token here the exposition is public
/// wherever the PKG is. `None` keeps it open, which `exec` warns about at
/// startup: the same contract cryptify's `metrics_token` has.
#[derive(Clone)]
pub struct MetricsToken(pub Option<String>);

impl MetricsToken {
    /// An empty value is no token. A deployment that threads an optional
    /// variable into the environment sets it to the empty string when unset,
    /// and an empty token must not become one that an empty header matches.
    pub fn new(token: Option<String>) -> Self {
        MetricsToken(token.filter(|t| !t.is_empty()))
    }
}

/// Whether the request carries `Authorization: Bearer <expected>`.
fn presents(req: &HttpRequest, expected: &str) -> bool {
    let Some(presented) = req
        .headers()
        .get(AUTHORIZATION)
        .and_then(|value| value.to_str().ok())
        .and_then(|value| {
            value
                .strip_prefix("Bearer ")
                .or_else(|| value.strip_prefix("bearer "))
        })
        .map(str::trim)
    else {
        return false;
    };
    // Constant-time on the bytes. The length is not secret, and `ct_eq` on
    // slices of different lengths is false without comparing.
    presented.as_bytes().ct_eq(expected.as_bytes()).into()
}

pub async fn metrics(req: HttpRequest) -> Result<String, Error> {
    let expected = req
        .app_data::<Data<MetricsToken>>()
        .and_then(|token| token.0.as_deref());
    if let Some(expected) = expected {
        if !presents(&req, expected) {
            return Err(Error::MetricsTokenInvalid);
        }
    }

    let metric_families = prometheus::gather();
    let mut buffer = Vec::new();
    let encoder = TextEncoder::new();
    encoder
        .encode(&metric_families, &mut buffer)
        .map_err(Error::Prometheus)?;

    String::from_utf8(buffer).map_err(|_e| Error::Unexpected)
}

#[cfg(test)]
mod tests {
    use super::*;
    use actix_web::http::StatusCode;
    use actix_web::web::{self, resource};
    use actix_web::{test, App};

    async fn status(token: Option<&str>, authorization: Option<&str>) -> StatusCode {
        let app = test::init_service(
            App::new()
                .app_data(Data::new(MetricsToken::new(token.map(str::to_owned))))
                .service(resource("/metrics").route(web::get().to(metrics))),
        )
        .await;
        let mut req = test::TestRequest::get().uri("/metrics");
        if let Some(value) = authorization {
            req = req.insert_header((AUTHORIZATION, value));
        }
        test::call_service(&app, req.to_request()).await.status()
    }

    #[actix_web::test]
    async fn without_a_configured_token_metrics_is_open() {
        assert_eq!(status(None, None).await, StatusCode::OK);
    }

    #[actix_web::test]
    async fn an_empty_token_is_no_token() {
        assert_eq!(status(Some(""), None).await, StatusCode::OK);
        assert_eq!(MetricsToken::new(Some(String::new())).0, None);
    }

    #[actix_web::test]
    async fn a_configured_token_is_required() {
        assert_eq!(status(Some("s3cret"), None).await, StatusCode::UNAUTHORIZED);
        assert_eq!(
            status(Some("s3cret"), Some("Bearer wrong")).await,
            StatusCode::UNAUTHORIZED
        );
        assert_eq!(
            status(Some("s3cret"), Some("Bearer s3cre")).await,
            StatusCode::UNAUTHORIZED,
            "a prefix of the token is not the token"
        );
        assert_eq!(
            status(Some("s3cret"), Some("Bearer ")).await,
            StatusCode::UNAUTHORIZED
        );
        assert_eq!(
            status(Some("s3cret"), Some("s3cret")).await,
            StatusCode::UNAUTHORIZED,
            "the Bearer scheme is part of the contract"
        );
    }

    #[actix_web::test]
    async fn the_configured_token_opens_metrics() {
        assert_eq!(
            status(Some("s3cret"), Some("Bearer s3cret")).await,
            StatusCode::OK
        );
        assert_eq!(
            status(Some("s3cret"), Some("bearer s3cret")).await,
            StatusCode::OK
        );
    }
}
