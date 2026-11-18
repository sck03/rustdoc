//! Required identity fields are business rules, independent of list column order.
use serde_json::Value;

pub fn validate_name(kind: &str, value: &Value) -> Result<(), String> {
    let fields: &[&str] = match kind {
        "customers" => &["customerNameEN", "customerNameCN"],
        "payees" => &["name", "bankName"],
        "units" | "ports" => &["nameEN", "nameCN"],
        "products" => &["productCode", "nameEN"],
        "crm-contacts" | "supplier-contacts" => &["name", "title"],
        "crm-follow-ups" => &["summary", "type"],
        "opportunities" => &["title", "quotationNo"],
        "supplier-products" => &["productCode", "supplierProductCode"],
        "supplier-assessments" => &["assessmentDate", "qualityScore"],
        "users" => &["username", "fullName"],
        "attachment-categories" => &["name", "versionNumber"],
        _ => return Err("业务资料类型缺少名称校验规则。".into()),
    };
    if fields.iter().any(|field| {
        value[*field]
            .as_str()
            .is_some_and(|text| !text.trim().is_empty())
    }) {
        Ok(())
    } else {
        Err("请填写名称或编号。".into())
    }
}
