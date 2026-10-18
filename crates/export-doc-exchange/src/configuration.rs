use crate::{DEFAULT_URL, Error, ErrorKind, Result, normalize};
use serde_json::Value;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Source {
    pub url: String,
    pub format: Format,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Format {
    Auto,
    BankOfChinaHtml,
    Icbc,
    Cmb,
    Json,
}
impl Format {
    pub fn name(self) -> &'static str {
        match self {
            Self::Auto => "Auto",
            Self::BankOfChinaHtml => "BankOfChinaHtml",
            Self::Icbc => "Icbc",
            Self::Cmb => "Cmb",
            Self::Json => "Json",
        }
    }
}

#[derive(Clone, Debug)]
pub struct Configuration {
    pub source: Source,
    pub minutes: u32,
    pub automatic: bool,
    pub selected: Vec<String>,
}
impl Configuration {
    pub fn read(value: &Value) -> Result<Self> {
        let invalid = |message| Error::new(ErrorKind::Invalid, message);
        if !value.is_object() {
            return Err(invalid("汇率设置必须是对象。"));
        }
        for (key, valid) in [
            ("url", value["url"].is_string()),
            ("sourceFormat", value["sourceFormat"].is_string()),
            (
                "cacheDurationMinutes",
                value["cacheDurationMinutes"].is_u64(),
            ),
            (
                "autoRefreshEnabled",
                value["autoRefreshEnabled"].is_boolean(),
            ),
            ("selectedCurrencies", value["selectedCurrencies"].is_array()),
        ] {
            if value.get(key).is_some() && !valid {
                return Err(Error::new(
                    ErrorKind::Invalid,
                    format!("汇率设置 {key} 的数据类型无效。"),
                ));
            }
        }
        let url = normalize(value["url"].as_str().unwrap_or(DEFAULT_URL))?.to_string();
        let format = match value["sourceFormat"].as_str().unwrap_or("BankOfChinaHtml") {
            "Auto" => Format::Auto,
            "BankOfChinaHtml" => Format::BankOfChinaHtml,
            "Icbc" => Format::Icbc,
            "Cmb" => Format::Cmb,
            "Json" => Format::Json,
            _ => return Err(invalid("不支持的汇率源格式。")),
        };
        crate::banks::validate_source(&url, format)?;
        let minutes = value["cacheDurationMinutes"].as_u64().unwrap_or(30);
        let automatic = value["autoRefreshEnabled"].as_bool().unwrap_or(false);
        if !(1..=1440).contains(&minutes) || (automatic && minutes < 30) {
            return Err(invalid(
                "缓存时间须为 1–1440 分钟；自动获取间隔至少 30 分钟。",
            ));
        }
        let mut selected = Vec::new();
        if let Some(rows) = value["selectedCurrencies"].as_array() {
            if rows.len() > 15 {
                return Err(invalid("最多选择 15 种常用货币。"));
            }
            for row in rows {
                let name = row
                    .as_str()
                    .ok_or_else(|| invalid("货币名称必须是文本。"))?
                    .trim();
                if name.is_empty() || name.chars().count() > 80 {
                    return Err(invalid("货币名称必须为 1–80 个字符。"));
                }
                if !selected.iter().any(|value| value == name) {
                    selected.push(name.to_owned());
                }
            }
        }
        Ok(Self {
            source: Source { url, format },
            minutes: minutes as u32,
            automatic,
            selected,
        })
    }
}
