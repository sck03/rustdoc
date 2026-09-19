//! Shared CRM vocabulary and text validation, independent of host and storage.
use unicode_normalization::UnicodeNormalization;

pub const CUSTOMER_STATUSES: &[&str] = &["潜在客户", "跟进中", "已成交", "暂停", "已流失"];
pub const FOLLOW_UP_TYPES: &[&str] = &["邮件", "电话", "拜访", "报价", "其他"];
pub fn clean(value: &str) -> String {
    value.trim().nfc().collect()
}
pub fn text(value: &str, label: &str, maximum: usize, required: bool) -> Result<String, String> {
    let value = clean(value);
    if required && value.is_empty() {
        return Err(format!("{label}不能为空。"));
    }
    if value.chars().count() > maximum {
        return Err(format!("{label}不能超过 {maximum} 个字符。"));
    }
    Ok(value)
}
pub fn choice(value: &str, choices: &[&str], default: &str, label: &str) -> Result<String, String> {
    let value = clean(value);
    if value.is_empty() {
        return Ok(default.into());
    }
    if choices.contains(&value.as_str()) {
        Ok(value)
    } else {
        Err(format!("{label}无效。"))
    }
}
