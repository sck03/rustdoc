use chrono::{DateTime, NaiveDate, NaiveDateTime, TimeZone, Utc};
use chrono_tz::Tz;

#[derive(Clone, Debug)]
pub struct BusinessClock {
    zone: Tz,
}

pub struct BusinessTime {
    pub today: NaiveDate,
    pub utc_now: DateTime<Utc>,
    pub valid_until: DateTime<Utc>,
    pub time_zone: &'static str,
}

impl Default for BusinessClock {
    fn default() -> Self {
        Self {
            zone: chrono_tz::Asia::Shanghai,
        }
    }
}
impl BusinessClock {
    pub fn local_input(&self, instant: &str) -> Result<String, String> {
        DateTime::parse_from_rfc3339(instant)
            .map(|time| {
                time.with_timezone(&self.zone)
                    .format("%Y-%m-%d %H:%M")
                    .to_string()
            })
            .map_err(|_| "时间必须包含明确的时区。".into())
    }
    pub fn parse_local_input(&self, value: &str) -> Result<DateTime<Utc>, String> {
        let local = NaiveDateTime::parse_from_str(value.trim(), "%Y-%m-%d %H:%M")
            .or_else(|_| NaiveDateTime::parse_from_str(value.trim(), "%Y-%m-%dT%H:%M"))
            .map_err(|_| "请输入完整时间，格式为 YYYY-MM-DD HH:MM。".to_owned())?;
        self.resolve_local(local)
    }
    pub fn resolve_local(&self, local: NaiveDateTime) -> Result<DateTime<Utc>, String> {
        match self.zone.from_local_datetime(&local) {
            chrono::LocalResult::Single(time) => Ok(time.with_timezone(&Utc)),
            chrono::LocalResult::Ambiguous(_, _) => {
                Err("此时间处于夏令时回拨的重复时段，请选择无歧义的时间。".into())
            }
            chrono::LocalResult::None => {
                Err("此时间处于夏令时跳过的时段，请选择有效的时间。".into())
            }
        }
    }
    pub fn day_range(&self, date: &str) -> Result<(String, String), String> {
        let day = NaiveDate::parse_from_str(date, "%Y-%m-%d")
            .map_err(|_| "请输入有效日期，格式为 YYYY-MM-DD。".to_owned())?;
        if day.to_string() != date {
            return Err("日期必须使用 YYYY-MM-DD 格式。".into());
        }
        let next = day.succ_opt().ok_or("日期超出范围。")?;
        Ok((
            self.parse_local_input(&format!("{day} 00:00"))?
                .to_rfc3339(),
            self.parse_local_input(&format!("{next} 00:00"))?
                .to_rfc3339(),
        ))
    }
    pub fn new(zone: &str) -> Result<Self, String> {
        zone.parse()
            .map(|zone| Self { zone })
            .map_err(|_| "业务时区必须是有效的 IANA 时区名称。".into())
    }
    pub fn now(&self) -> Result<BusinessTime, String> {
        self.at(Utc::now())
    }
    pub fn at(&self, now: DateTime<Utc>) -> Result<BusinessTime, String> {
        let today = now.with_timezone(&self.zone).date_naive();
        let tomorrow = today.succ_opt().ok_or("业务日期超出范围。")?;
        let midnight = tomorrow.and_hms_opt(0, 0, 0).ok_or("业务日期无效。")?;
        let valid_until = self
            .zone
            .from_local_datetime(&midnight)
            .earliest()
            .ok_or("业务时区的次日零点不存在。")?
            .with_timezone(&Utc);
        Ok(BusinessTime {
            today,
            utc_now: now,
            valid_until,
            time_zone: self.zone.name(),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn local_appointments_roundtrip_without_using_the_host_time_zone() {
        let zone = BusinessClock::new("America/New_York").unwrap();
        assert_eq!(
            zone.parse_local_input("2026-09-17 09:30")
                .unwrap()
                .to_rfc3339(),
            "2026-09-17T13:30:00+00:00"
        );
        assert_eq!(
            zone.local_input("2026-09-17T13:30:00Z").unwrap(),
            "2026-09-17 09:30"
        );
        assert!(zone.parse_local_input("2026-03-08 02:30").is_err());
        assert!(zone.parse_local_input("2026-11-01 01:30").is_err());
        let (from, to) = zone.day_range("2026-03-08").unwrap();
        assert_eq!(
            (DateTime::parse_from_rfc3339(&to).unwrap()
                - DateTime::parse_from_rfc3339(&from).unwrap())
            .num_hours(),
            23
        );
    }
    #[test]
    fn date_expiry_is_the_next_local_midnight_including_dst() {
        let utc = "2026-09-16T15:59:00Z".parse().unwrap();
        let time = BusinessClock::default().at(utc).unwrap();
        assert_eq!(time.today.to_string(), "2026-09-16");
        assert_eq!(time.valid_until.to_rfc3339(), "2026-09-16T16:00:00+00:00");
        let time = BusinessClock::new("America/New_York")
            .unwrap()
            .at("2026-03-08T05:00:00Z".parse().unwrap())
            .unwrap();
        assert_eq!((time.valid_until - time.utc_now).num_hours(), 23);
        assert!(BusinessClock::new("invalid/timezone").is_err());
    }
}
