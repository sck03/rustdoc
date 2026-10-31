//! Versioned civil calendars. Working time is accumulated on the UTC timeline,
//! with explicit IANA zones and strict dates; no host clock or filesystem access.
use chrono::{DateTime, Datelike, Duration, NaiveDate, NaiveTime, TimeZone, Utc};
use chrono_tz::Tz;
use serde::{Deserialize, Serialize};
use std::collections::HashSet;

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum DeadlineMode {
    #[default]
    Natural,
    Working,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct WorkInterval {
    pub starts_at: String,
    pub ends_at: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct BusinessCalendar {
    pub version: u32,
    pub time_zone: String,
    pub working_weekdays: Vec<u8>,
    pub intervals: Vec<WorkInterval>,
    pub holidays: Vec<String>,
    pub additional_working_days: Vec<String>,
}
impl Default for BusinessCalendar {
    fn default() -> Self {
        Self {
            version: 1,
            time_zone: "Asia/Shanghai".into(),
            working_weekdays: vec![1, 2, 3, 4, 5],
            intervals: vec![
                WorkInterval {
                    starts_at: "09:00".into(),
                    ends_at: "12:00".into(),
                },
                WorkInterval {
                    starts_at: "13:00".into(),
                    ends_at: "18:00".into(),
                },
            ],
            holidays: vec![],
            additional_working_days: vec![],
        }
    }
}
fn time(value: &str) -> Result<NaiveTime, String> {
    let t = NaiveTime::parse_from_str(value, "%H:%M")
        .map_err(|_| "工作时段须使用 HH:MM。".to_owned())?;
    if t.format("%H:%M").to_string() != value {
        return Err("工作时段须使用 HH:MM。".into());
    }
    Ok(t)
}
fn date(value: &str) -> Result<NaiveDate, String> {
    crate::business_date::parse(value)
}
impl BusinessCalendar {
    pub fn validate(&self) -> Result<(), String> {
        self.time_zone
            .parse::<Tz>()
            .map_err(|_| "工作日历须使用有效 IANA 时区。")?;
        if self.version == 0
            || self.working_weekdays.is_empty()
            || self.working_weekdays.len() > 7
            || self.intervals.is_empty()
            || self.intervals.len() > 8
            || self.holidays.len() > 1000
            || self.additional_working_days.len() > 1000
        {
            return Err("日历版本、工作日或时段数量无效。".into());
        }
        let mut days = HashSet::new();
        if self
            .working_weekdays
            .iter()
            .any(|d| !(1..=7).contains(d) || !days.insert(d))
        {
            return Err("工作日须为不重复的周一至周日。".into());
        }
        let mut previous = None;
        for interval in &self.intervals {
            let start = time(&interval.starts_at)?;
            let end = time(&interval.ends_at)?;
            if end <= start || previous.is_some_and(|p| p > start) {
                return Err("工作时段须按时间排列且不重叠，不能跨自然日。".into());
            }
            previous = Some(end);
        }
        let mut holidays = HashSet::new();
        for d in &self.holidays {
            if !holidays.insert(date(d)?) {
                return Err("节假日不能重复。".into());
            }
        }
        let mut additions = HashSet::new();
        for d in &self.additional_working_days {
            let d = date(d)?;
            if !additions.insert(d) || holidays.contains(&d) {
                return Err("调休日不能重复或同时列为假日。".into());
            }
        }
        Ok(())
    }
    fn segments(
        &self,
        day: NaiveDate,
        zone: Tz,
    ) -> Result<Vec<(DateTime<Utc>, DateTime<Utc>)>, String> {
        let text = day.to_string();
        let active = self.additional_working_days.contains(&text)
            || (!self.holidays.contains(&text)
                && self
                    .working_weekdays
                    .contains(&(day.weekday().number_from_monday() as u8)));
        if !active {
            return Ok(vec![]);
        }
        let resolve = |time: NaiveTime| {
            zone.from_local_datetime(&day.and_time(time))
                .single()
                .map(|t| t.with_timezone(&Utc))
                .ok_or_else(|| "工作时段落在夏令时重复或跳过的时间，请修正日历。".to_owned())
        };
        self.intervals
            .iter()
            .map(|interval| {
                Ok((
                    resolve(time(&interval.starts_at)?)?,
                    resolve(time(&interval.ends_at)?)?,
                ))
            })
            .collect()
    }
    pub fn deadline(&self, start: DateTime<Utc>, seconds: i64) -> Result<DateTime<Utc>, String> {
        self.validate()?;
        if seconds <= 0 || seconds > 8760 * 3600 {
            return Err("工作时限须为 1–8760 小时范围内的正时长。".into());
        }
        let zone = self.time_zone.parse::<Tz>().map_err(|_| "日历时区无效。")?;
        let mut day = start.with_timezone(&zone).date_naive();
        let mut remaining = seconds;
        for _ in 0..5000 {
            for (begin, end) in self.segments(day, zone)? {
                let begin = begin.max(start);
                if end <= begin {
                    continue;
                }
                let available = (end - begin).num_seconds();
                if available >= remaining {
                    return begin
                        .checked_add_signed(Duration::seconds(remaining))
                        .ok_or_else(|| "日历期限超出日期范围。".into());
                }
                remaining -= available;
            }
            day = day.succ_opt().ok_or("日历期限超出日期范围。")?;
        }
        Err("工作日历在最长计算范围内无法满足时限。".into())
    }
    pub fn elapsed(&self, start: DateTime<Utc>, end: DateTime<Utc>) -> Result<i64, String> {
        self.validate()?;
        if end < start {
            return Err("结束时间早于开始时间。".into());
        }
        let zone = self.time_zone.parse::<Tz>().map_err(|_| "日历时区无效。")?;
        let mut day = start.with_timezone(&zone).date_naive();
        let last = end.with_timezone(&zone).date_naive();
        let mut seconds = 0i64;
        for _ in 0..5000 {
            for (begin, finish) in self.segments(day, zone)? {
                let begin = begin.max(start);
                let finish = finish.min(end);
                if finish > begin {
                    seconds = seconds
                        .checked_add((finish - begin).num_seconds())
                        .ok_or("工作时长累计溢出。")?;
                }
            }
            if day >= last {
                return Ok(seconds);
            }
            day = day.succ_opt().ok_or("日历日期超出范围。")?;
        }
        Err("工作时长计算超过日历范围。".into())
    }
}
