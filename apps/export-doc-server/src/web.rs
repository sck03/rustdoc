//! Static web policy is independent of endpoint authorization metadata.
use axum::{extract::Request, http::header, middleware::Next, response::Response};

pub(crate) async fn policy(request: Request, next: Next) -> Response {
    let path = request.uri().path();
    let immutable = hashed_asset(path);
    let mut response = if path == "/api" || path.starts_with("/api/") {
        crate::response::error(crate::request::error(404, "接口不存在。"))
    } else {
        next.run(request).await
    };
    let immutable = immutable
        && response.status().is_success()
        && response
            .headers()
            .get(header::CONTENT_TYPE)
            .is_some_and(|value| !value.as_bytes().starts_with(b"text/html"));
    let headers = response.headers_mut();
    headers.insert(header::X_CONTENT_TYPE_OPTIONS, "nosniff".parse().unwrap());
    headers.insert(header::X_FRAME_OPTIONS, "DENY".parse().unwrap());
    headers.insert(
        header::REFERRER_POLICY,
        "strict-origin-when-cross-origin".parse().unwrap(),
    );
    headers.insert(header::CONTENT_SECURITY_POLICY,
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' blob:; connect-src 'self'; frame-src 'self' blob:; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'".parse().unwrap());
    headers.insert(
        header::CACHE_CONTROL,
        if immutable {
            "public, max-age=31536000, immutable"
        } else {
            "no-cache"
        }
        .parse()
        .unwrap(),
    );
    response
}

fn hashed_asset(path: &str) -> bool {
    let Some(file) = path
        .strip_prefix("/assets/")
        .filter(|file| !file.contains('/'))
    else {
        return false;
    };
    let Some((stem, extension)) = file.rsplit_once('.') else {
        return false;
    };
    matches!(
        extension,
        "js" | "css" | "woff2" | "svg" | "png" | "webp" | "jpg"
    ) && stem.len() > 9
        && stem.is_ascii()
        && stem.as_bytes()[stem.len() - 9] == b'-'
        && stem.as_bytes()[stem.len() - 8..]
            .iter()
            .all(|c| c.is_ascii_alphanumeric() || matches!(c, b'_' | b'-'))
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::{Router, body::Body, http::StatusCode, middleware, routing::get};
    use tower::ServiceExt;
    #[tokio::test]
    async fn unknown_api_never_falls_back_to_html_and_only_hashed_assets_are_immutable() {
        let router = Router::new()
            .route(
                "/assets/app-Abcd1234.js",
                get(|| async { ([(header::CONTENT_TYPE, "text/javascript")], "script") }),
            )
            .fallback(|| async { ([(header::CONTENT_TYPE, "text/html")], "index") })
            .layer(middleware::from_fn(policy));
        for method in ["GET", "POST", "DELETE"] {
            let response = router
                .clone()
                .oneshot(
                    Request::builder()
                        .method(method)
                        .uri("/api/unknown")
                        .body(Body::empty())
                        .unwrap(),
                )
                .await
                .unwrap();
            assert_eq!(response.status(), StatusCode::NOT_FOUND);
            assert!(
                response.headers()[header::CONTENT_TYPE]
                    .to_str()
                    .unwrap()
                    .starts_with("application/json")
            );
        }
        for (path, cache) in [
            ("/invoices", "no-cache"),
            ("/assets/missing-Abcd1234.js", "no-cache"),
            (
                "/assets/app-Abcd1234.js",
                "public, max-age=31536000, immutable",
            ),
        ] {
            let response = router
                .clone()
                .oneshot(Request::builder().uri(path).body(Body::empty()).unwrap())
                .await
                .unwrap();
            assert_eq!(response.headers()[header::CACHE_CONTROL], cache);
            assert_eq!(
                response.headers()[header::X_CONTENT_TYPE_OPTIONS],
                "nosniff"
            );
            assert!(
                response
                    .headers()
                    .contains_key(header::CONTENT_SECURITY_POLICY)
            );
        }
    }
}
