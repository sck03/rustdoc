//! Archival retention is based on a confirmed business date, never upload age.
use chrono::{Days, NaiveDate};
pub fn expires_on(basis: Option<&str>, days: u32) -> Result<Option<NaiveDate>, String> {
    if days > 36500 {
        return Err("档案保留期限不能超过一百年。".into());
    }
    if days == 0 {
        return Ok(None);
    }
    let Some(basis) = basis.filter(|v| !v.is_empty()) else {
        return Ok(None);
    };
    if basis.len() != 10 {
        return Err("归档依据日期必须为 YYYY-MM-DD。".into());
    }
    let day = NaiveDate::parse_from_str(basis, "%Y-%m-%d").map_err(|_| "归档依据日期无效。")?;
    day.checked_add_days(Days::new(days as u64))
        .map(Some)
        .ok_or_else(|| "保留期限超出日期范围。".into())
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn missing_basis_and_permanent_policy_do_not_expire() {
        assert_eq!(expires_on(None, 30).unwrap(), None);
        assert_eq!(expires_on(Some("2026-01-01"), 0).unwrap(), None);
        assert_eq!(
            expires_on(Some("2024-02-29"), 365)
                .unwrap()
                .unwrap()
                .to_string(),
            "2025-02-28"
        );
        assert!(expires_on(Some("2026-02-30"), 1).is_err());
    }
}
