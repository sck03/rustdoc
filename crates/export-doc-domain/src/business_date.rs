//! Strict natural days shared by business validation and report conditions.
use chrono::{Datelike, NaiveDate};

pub fn parse(value: &str) -> Result<NaiveDate, String> {
    if value.len() != 10
        || !value.bytes().enumerate().all(|(index, byte)| {
            if index == 4 || index == 7 {
                byte == b'-'
            } else {
                byte.is_ascii_digit()
            }
        })
    {
        return Err("日期须为有效的 YYYY-MM-DD。".into());
    }
    NaiveDate::parse_from_str(value, "%Y-%m-%d")
        .ok()
        .filter(|date| date.year() > 0)
        .ok_or_else(|| "日期须为有效的 YYYY-MM-DD。".into())
}

pub fn is_valid(value: &str) -> bool {
    parse(value).is_ok()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn natural_days_reject_signs_non_digits_and_invalid_calendar_dates() {
        for value in [
            "2026-+1-+1",
            "+123-01-01",
            "0000-01-01",
            "2026-02-29",
            "2026-09-31",
            "2026-1-01",
            "２０２６-01-01",
            "2026-01-01 ",
        ] {
            assert!(!is_valid(value), "{value}");
        }
        for value in ["0001-01-01", "2024-02-29", "2026-10-27", "9999-12-31"] {
            assert_eq!(parse(value).unwrap().format("%Y-%m-%d").to_string(), value);
        }
    }
}
