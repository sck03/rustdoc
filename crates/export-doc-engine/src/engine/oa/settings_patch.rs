//! Scoped optimistic updates; unrelated drafts and editors do not share a revision.
use super::*;

pub(super) fn revision(settings: &Value, key: &str) -> i64 {
    settings["revisions"][key].as_i64().unwrap_or(0)
}
pub(super) fn merge(previous: &Value, body: &Value) -> Result<Value> {
    let section = text(body, "section");
    if ![
        "rules",
        "calendar",
        "delegations",
        "handlingServices",
        "sheetTemplates",
    ]
    .contains(&section.as_str())
    {
        return Err(invalid("设置分区无效。"));
    }
    let key = text(body, "definitionKey");
    if section != "rules" && body["remove"] == true {
        return Err(invalid("仅未发布事项支持删除。"));
    }
    let revision_key = if section == "rules" {
        format!("rule:{key}")
    } else {
        section.clone()
    };
    if body["expectedRevision"].as_i64() != Some(revision(previous, &revision_key)) {
        return Err(conflict("当前分区或流程已被修改，请重新载入后核对。"));
    }
    let mut merged = previous.clone();
    if section == "rules" {
        let rules = merged["rules"]
            .as_array_mut()
            .ok_or_else(|| super::super::error::unavailable("流程目录损坏。"))?;
        let index = rules.iter().position(|r| r["key"] == key);
        if body["remove"] == true {
            let index = index.ok_or_else(|| error(404, "流程草稿不存在。"))?;
            if rules[index]["form"].is_null()
                || previous["publications"]
                    .as_array()
                    .is_some_and(|items| items.iter().any(|p| p["key"] == key))
            {
                return Err(conflict("只能删除未发布的自定义事项；已发布事项请停用。"));
            }
            rules.remove(index);
        } else {
            if body["rule"]["key"] != key {
                return Err(invalid("流程编号不一致。"));
            }
            if let Some(index) = index {
                rules[index] = body["rule"].clone();
            } else {
                rules.push(body["rule"].clone());
            }
        }
    } else {
        if body.get(&section).is_none() {
            return Err(invalid("缺少当前设置分区的内容。"));
        }
        merged[&section] = body[&section].clone();
    }
    merged["expectedVersion"] = previous["versionNumber"].clone();
    Ok(merged)
}
