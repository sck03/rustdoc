//! Directory import fields and normalization are shared with interactive editors.
use crate::crm;
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub const SUPPLIER_STATUSES: &[&str] = &["合作中", "考察中", "暂停", "停用"];
pub const COMMON_FIELDS: &[(&str, &str, usize)] = &[
    ("name", "名称", 200),
    ("countryRegion", "国家/地区", 100),
    ("website", "网站", 300),
    ("status", "状态", 30),
    ("notes", "备注", 1000),
    ("contactName", "联系人姓名", 100),
    ("contactTitle", "联系人职位", 100),
    ("contactEmail", "联系人邮箱", 200),
    ("contactPhone", "联系人电话", 100),
];
pub fn header(value: &str) -> Option<&'static str> {
    let key: String = crm::clean(value)
        .chars()
        .filter(|ch| ch.is_alphanumeric())
        .flat_map(char::to_lowercase)
        .collect();
    Some(match key.as_str() {
        "客户名称" | "供应商名称" | "公司名称" | "customername" | "suppliername" | "name" => {
            "name"
        }
        "国家地区" | "国家" | "countryregion" | "country" => "countryRegion",
        "网站" | "网址" | "website" => "website",
        "状态" | "status" => "status",
        "来源" | "source" => "source",
        "备注" | "notes" => "notes",
        "分类" | "category" => "category",
        "主要产品" | "产品" | "mainproducts" => "mainProducts",
        "联系人" | "主要联系人" | "联系人姓名" | "contactname" | "contact" => {
            "contactName"
        }
        "职位" | "联系人职位" | "contacttitle" | "title" => "contactTitle",
        "邮箱" | "联系人邮箱" | "contactemail" | "email" => "contactEmail",
        "电话" | "联系人电话" | "contactphone" | "phone" => "contactPhone",
        _ => return None,
    })
}
pub fn map_rows(table: &[Vec<String>], supplier: bool) -> Result<Vec<Value>, String> {
    if table.len() < 2 {
        return Err("导入文件至少需要表头和一行数据。".into());
    }
    let mut columns = BTreeMap::new();
    for (index, value) in table[0].iter().enumerate() {
        if let Some(key) = header(value) {
            columns.entry(key).or_insert(index);
        }
    }
    if !columns.contains_key("name") {
        return Err("导入文件缺少名称列。".into());
    }
    Ok(table
        .iter()
        .enumerate()
        .skip(1)
        .filter(|(_, row)| row.iter().any(|value| !value.trim().is_empty()))
        .map(|(index, row)| {
            let mut value = json!({"rowNumber":index+1});
            for (key, column) in &columns {
                value[*key] = json!(row.get(*column).map(String::as_str).unwrap_or(""));
            }
            normalize(&value, supplier)
        })
        .collect())
}
pub fn normalize(row: &Value, supplier: bool) -> Value {
    let mut result = json!({"rowNumber":row["rowNumber"], "isDuplicate":false});
    let mut errors = vec![];
    let extra: &[(&str, &str, usize)] = if supplier {
        &[("category", "分类", 100), ("mainProducts", "主要产品", 500)]
    } else {
        &[("source", "来源", 50)]
    };
    for &(key, label, limit) in COMMON_FIELDS.iter().chain(extra) {
        let value = row[key].as_str().unwrap_or("");
        result[key] = json!(crm::clean(value));
        if let Err(cause) = crm::text(value, label, limit, key == "name") {
            errors.push(cause);
        }
    }
    match crm::choice(
        result["status"].as_str().unwrap_or(""),
        if supplier {
            SUPPLIER_STATUSES
        } else {
            crm::CUSTOMER_STATUSES
        },
        if supplier {
            "合作中"
        } else {
            "潜在客户"
        },
        "状态",
    ) {
        Ok(status) => result["status"] = json!(status),
        Err(cause) => errors.push(cause),
    }
    let email = result["contactEmail"].as_str().unwrap_or("");
    if !email.is_empty() && !mailbox(email) {
        errors.push("联系人邮箱格式无效。".into());
    }
    if result["contactName"] == ""
        && ["contactTitle", "contactEmail", "contactPhone"]
            .iter()
            .any(|key| result[*key] != "")
    {
        errors.push("填写联系人职位、邮箱或电话时必须同时填写联系人姓名。".into());
    }
    result["error"] = json!(errors.join(" "));
    result
}
fn mailbox(value: &str) -> bool {
    if value.chars().any(char::is_control) {
        return false;
    }
    let Some((local, domain)) = value.rsplit_once('@') else {
        return false;
    };
    if local.is_empty()
        || domain.is_empty()
        || domain
            .chars()
            .any(|ch| ch.is_whitespace() || "@<>(),;\\\"".contains(ch))
    {
        return false;
    }
    if local.starts_with('"') && local.ends_with('"') && local.len() >= 2 {
        let mut escape = false;
        for ch in local[1..local.len() - 1].chars() {
            if escape {
                escape = false;
            } else if ch == '\\' {
                escape = true;
            } else if ch == '"' {
                return false;
            }
        }
        !escape
    } else {
        !local.starts_with('.')
            && !local.ends_with('.')
            && !local.contains("..")
            && !local
                .chars()
                .any(|ch| ch.is_whitespace() || "@<>(),:;\\[]\"".contains(ch))
    }
}
