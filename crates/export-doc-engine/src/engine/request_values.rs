//! Shared lookup for generated route/query arguments; callers own validation and defaults.
pub(super) fn find<'a>(values: &'a [(&str, String)], name: &str) -> Option<&'a str> {
    values
        .iter()
        .find(|(key, _)| *key == name)
        .map(|(_, value)| value.as_str())
}
