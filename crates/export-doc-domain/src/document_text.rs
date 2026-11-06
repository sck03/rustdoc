//! Text shared by business documents and XML based preview/PDF renderers.
//! Reject unsupported characters; never silently alter business wording.
pub fn is_valid(value: &str) -> bool {
    !value.chars().any(|c| {
        (c.is_control() && !matches!(c, '\r' | '\n' | '\t')) || matches!(c, '\u{fffe}' | '\u{ffff}')
    })
}
