use chrono::{TimeZone, Utc};
use export_doc_domain::business_calendar::{BusinessCalendar, WorkInterval};
#[test]
fn work_deadlines_skip_lunch_weekends_and_holidays_and_honor_adjusted_workdays() {
    let mut calendar = BusinessCalendar::default();
    let friday = Utc.with_ymd_and_hms(2026, 10, 30, 9, 0, 0).unwrap(); // Friday 17:00 Shanghai.
    assert_eq!(
        calendar.deadline(friday, 2 * 3600).unwrap(),
        Utc.with_ymd_and_hms(2026, 11, 2, 2, 0, 0).unwrap()
    );
    calendar.holidays.push("2026-11-02".into());
    assert_eq!(
        calendar.deadline(friday, 2 * 3600).unwrap(),
        Utc.with_ymd_and_hms(2026, 11, 3, 2, 0, 0).unwrap()
    );
    calendar.additional_working_days.push("2026-10-31".into());
    assert_eq!(
        calendar.deadline(friday, 2 * 3600).unwrap(),
        Utc.with_ymd_and_hms(2026, 10, 31, 2, 0, 0).unwrap()
    );
    assert_eq!(
        calendar
            .elapsed(friday, calendar.deadline(friday, 8 * 3600).unwrap())
            .unwrap(),
        8 * 3600
    );
}
#[test]
fn calendar_versions_and_dst_are_explicit_and_invalid_days_are_rejected() {
    let mut calendar = BusinessCalendar::default();
    calendar.time_zone = "America/New_York".into();
    calendar.working_weekdays = vec![7];
    calendar.intervals = vec![WorkInterval {
        starts_at: "01:30".into(),
        ends_at: "03:30".into(),
    }];
    let start = Utc.with_ymd_and_hms(2026, 11, 1, 4, 0, 0).unwrap();
    assert!(
        calendar.deadline(start, 3600).is_err(),
        "DST repeated boundary cannot be guessed"
    );
    calendar.holidays = vec!["2026-02-30".into()];
    assert!(calendar.validate().is_err());
    calendar.holidays.clear();
    calendar.time_zone = "not-a-zone".into();
    assert!(calendar.validate().is_err());
}
