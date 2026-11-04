//! Response shapes share one status and error definition.
use super::model::*;

const BUSINESS_ERRORS: &[&str] = &["400", "403", "404", "409", "503", "504"];
const SESSION_ERRORS: &[&str] = &["400", "401", "403", "404", "409", "429", "503", "504"];

pub(super) fn secured_json(schema: Schema) -> Responses {
    json(schema, SESSION_ERRORS, &[])
}
pub(super) fn business_json(schema: Schema) -> Responses {
    json(schema, BUSINESS_ERRORS, &[])
}
pub(super) fn json(schema: Schema, errors: &[&'static str], empty: &[&'static str]) -> Responses {
    reply("200", "application/json", Some(schema), errors, empty)
}
pub(super) fn reply(
    code: &'static str,
    mime: &'static str,
    schema: Option<Schema>,
    errors: &[&'static str],
    empty: &[&'static str],
) -> Responses {
    let first = schema.map_or_else(|| bare(code), |schema| typed(code, mime, schema));
    std::iter::once(first)
        .chain(errors.iter().map(|code| error(code)))
        .chain(empty.iter().map(|code| bare(code)))
        .collect()
}
pub(super) fn empty(codes: &[&'static str]) -> Responses {
    codes.iter().map(|code| bare(code)).collect()
}
pub(super) fn error(code: &'static str) -> (&'static str, Response) {
    typed(code, "application/json", r("ApiErrorResponse"))
}
pub(super) fn typed(
    code: &'static str,
    mime: &'static str,
    schema: Schema,
) -> (&'static str, Response) {
    (
        code,
        Response {
            description: description(code),
            content: Some(Content::from([(mime, Media { schema })])),
        },
    )
}
pub(super) fn bare(code: &'static str) -> (&'static str, Response) {
    (
        code,
        Response {
            description: description(code),
            content: None,
        },
    )
}
fn description(code: &str) -> &'static str {
    match code {
        "200" => "OK",
        "201" => "Created",
        "202" => "Accepted",
        "204" => "No Content",
        "400" => "Bad Request",
        "401" => "Unauthorized",
        "403" => "Forbidden",
        "404" => "Not Found",
        "409" => "Conflict",
        "413" => "Payload Too Large",
        "429" => "Too Many Requests",
        "500" => "Internal Server Error",
        "503" => "Service Unavailable",
        "504" => "Gateway Timeout",
        _ => panic!("undeclared native response status"),
    }
}
