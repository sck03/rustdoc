use chrono::{DateTime, SecondsFormat, Utc};

pub fn validity(starts_at: &str, expires_at: &str) -> Result<(String, String), String> {
    let parse = |s| {
        DateTime::parse_from_rfc3339(s)
            .map(|t| t.with_timezone(&Utc))
            .map_err(|_| "公告有效期必须包含日期、时间和时区。".to_owned())
    };
    let start = parse(starts_at)?;
    let end = parse(expires_at)?;
    if end <= start {
        return Err("失效时间必须晚于生效时间。".into());
    }
    Ok((
        start.to_rfc3339_opts(SecondsFormat::Nanos, true),
        end.to_rfc3339_opts(SecondsFormat::Nanos, true),
    ))
}
pub fn next_status(status: &str, action: &str) -> Option<&'static str> {
    match (status, action) {
        ("Draft" | "Withdrawn", "publish") => Some("Published"),
        ("Published", "withdraw") => Some("Withdrawn"),
        ("Draft" | "Withdrawn" | "Published", "archive") => Some("Archived"),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn expiry_compares_instants_and_lifecycle_freezes_published_content() {
        assert!(validity("2026-09-30T12:00:00", "2026-10-01T00:00:00Z").is_err());
        assert!(validity("2026-09-30T12:00:00+08:00", "2026-09-30T03:59:59Z").is_err());
        assert_eq!(
            validity("2026-09-30T12:00:00+08:00", "2026-09-30T05:00:00Z")
                .unwrap()
                .0,
            "2026-09-30T04:00:00.000000000Z"
        );
        assert_eq!(next_status("Published", "publish"), None);
        assert_eq!(next_status("Archived", "publish"), None);
        assert_eq!(next_status("Withdrawn", "publish"), Some("Published"));
    }

    #[test]
    fn validity_preserves_submillisecond_intervals() {
        let (start, end) = validity(
            "2026-11-07T08:00:00.123456788+08:00",
            "2026-11-07T00:00:00.123456789Z",
        )
        .unwrap();
        assert_eq!(start, "2026-11-07T00:00:00.123456788Z");
        assert_eq!(end, "2026-11-07T00:00:00.123456789Z");
        assert!(validity(&start, &end).is_ok());
    }
}
