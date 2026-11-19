//! Shared lookup and identity parsing; callers own declared wire types and defaults.
pub(super) fn find<'a>(values: &'a [(&str, String)], name: &str) -> Option<&'a str> {
    values
        .iter()
        .find(|(key, _)| *key == name)
        .map(|(_, value)| value.as_str())
}

/// Callers validate the declared wire type and decide whether zero is unspecified.
pub(super) fn record_id(value: Option<&serde_json::Value>) -> super::error::Result<Option<i64>> {
    value
        .filter(|value| !value.is_null())
        .map(|value| {
            value
                .as_i64()
                .or_else(|| value.as_str()?.parse().ok())
                .filter(|id| *id >= 0)
                .ok_or_else(|| super::error::invalid("记录编号无效。"))
        })
        .transpose()
}
