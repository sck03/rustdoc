//! Editable grants stay independent of filtering and dependency explanations.
use export_doc_domain::permissions::{self, Grant};
use export_doc_engine::{contracts, generated_api::*};
use serde_json::{Value, json};
use std::collections::BTreeMap;

#[derive(Default)]
pub struct AccessModel {
    pub templates: Vec<Value>,
    pub draft: Option<Value>,
    pub baseline: Option<Value>,
    pub search: String,
    pub workspace: String,
}
impl AccessModel {
    pub fn open(&mut self, record: Option<Value>, copy: bool) {
        let mut draft =
            record.unwrap_or_else(|| contracts::object(CREATE_PERMISSION_TEMPLATE.id, true));
        draft["expectedVersion"] = if copy {
            json!(0)
        } else {
            draft["versionNumber"].clone()
        };
        if !draft["expectedVersion"].is_number() {
            draft["expectedVersion"] = json!(0);
        }
        if copy || draft["id"].as_i64().unwrap_or(0) == 0 {
            draft["id"] = json!(0);
            draft["isSystem"] = json!(false);
            draft["isActive"] = json!(true);
            draft["code"] = json!("");
            if copy {
                draft["name"] = json!(format!("{}（副本）", text(&draft, "name")));
            }
        }
        if !draft["grants"].is_array() {
            draft["grants"] = json!([]);
        }
        if copy {
            let grants: Vec<_> = self::grants(&draft)
                .into_iter()
                .filter(|grant| {
                    permissions::resource(&grant.resource_key)
                        .is_some_and(|resource| !resource.is_technical)
                })
                .collect();
            draft["grants"] = json!(grants);
        }
        self.baseline = Some(draft.clone());
        self.draft = Some(draft);
    }
    pub fn dirty(&self) -> bool {
        self.draft != self.baseline
    }
    pub fn read_only(&self) -> bool {
        self.draft
            .as_ref()
            .is_some_and(|draft| draft["isSystem"] == true && text(draft, "code") == "Admin")
    }
    pub fn edit(&mut self, key: &str, value: &str) {
        if self.read_only() {
            return;
        }
        if let Some(draft) = &mut self.draft {
            match key {
                "code" if draft["isSystem"] != true => draft[key] = json!(value),
                "name" | "description" => draft[key] = json!(value),
                "isActive" if draft["isSystem"] != true => draft[key] = json!(value == "true"),
                _ => {}
            }
        }
    }
    pub fn grant(&mut self, resource_key: &str, action: &str, scope: &str) -> Result<(), String> {
        if self.read_only() {
            return Err("系统管理员方案只读，可复制后建立新方案。".into());
        }
        let resource = permissions::resource(resource_key).ok_or("权限模块不存在。")?;
        if resource.is_technical
            || !resource.actions.iter().any(|item| item.key == action)
            || (!scope.is_empty() && permissions::scope_rank(scope) == 0)
        {
            return Err("请选择有效的业务权限和数据范围。".into());
        }
        let draft = self.draft.as_mut().ok_or("请先选择权限方案。")?;
        let mut grants = self::grants(draft);
        grants.retain(|grant| grant.resource_key != resource.key || grant.action != action);
        if !scope.is_empty() {
            grants.push(Grant {
                resource_key: resource.key.clone(),
                action: action.into(),
                data_scope: if resource.supports_data_scope {
                    scope.into()
                } else {
                    "all".into()
                },
            });
        }
        draft["grants"] = json!(permissions::normalize(&grants)?);
        Ok(())
    }
    pub fn preset(&mut self, key: &str, level: &str, scope: &str) -> Result<(), String> {
        let resource = permissions::resource(key).ok_or("权限模块不存在。")?;
        let rank = |level: &str| match level {
            "view" => 1,
            "operate" => 2,
            "manage" => 3,
            _ => 0,
        };
        for action in &resource.actions {
            self.grant(
                key,
                &action.key,
                if rank(&action.navigation_access_level) <= rank(level) {
                    scope
                } else {
                    ""
                },
            )?;
        }
        Ok(())
    }
    pub fn direct(&self) -> BTreeMap<(String, String), String> {
        self.draft
            .as_ref()
            .map(self::grants)
            .unwrap_or_default()
            .into_iter()
            .map(|grant| ((grant.resource_key, grant.action), grant.data_scope))
            .collect()
    }
    pub fn body(&self) -> Result<Value, String> {
        let draft = self.draft.as_ref().ok_or("请先选择权限方案。")?;
        if text(draft, "code").trim().is_empty() || text(draft, "name").trim().is_empty() {
            return Err("请填写方案代码和名称。".into());
        }
        if self.read_only() {
            return Err("系统管理员权限方案不可修改。".into());
        }
        let grants: Vec<_> = self::grants(draft)
            .into_iter()
            .filter(|grant| {
                permissions::resource(&grant.resource_key)
                    .is_some_and(|resource| !resource.is_technical)
            })
            .collect();
        Ok(
            json!({"id":draft["id"],"code":text(draft,"code"),"name":text(draft,"name"),
            "description":text(draft,"description"),"isActive":draft["isActive"],
            "grants":grants,"expectedVersion":draft["expectedVersion"]}),
        )
    }
}
pub fn text<'a>(value: &'a Value, key: &str) -> &'a str {
    value[key].as_str().unwrap_or("")
}
pub fn grants(value: &Value) -> Vec<Grant> {
    serde_json::from_value(value["grants"].clone()).unwrap_or_default()
}
pub fn scope_label(scope: &str) -> &'static str {
    match scope {
        "own" => "本人",
        "department" => "部门",
        "company" => "公司",
        "all" => "全部",
        _ => "未开放",
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn filtering_and_presets_keep_other_modules_and_exclude_technical_grants_from_copies() {
        let mut model = AccessModel::default();
        model.open(None, false);
        model.edit("name", "单证岗位");
        model.edit("code", "DOC");
        model.grant("document.invoices", "view", "company").unwrap();
        model.search = "客户".into();
        model.preset("sales.customers", "view", "own").unwrap();
        assert_eq!(
            model.direct()[&("document.invoices".into(), "view".into())],
            "company"
        );
        assert!(model.grant("system.users", "manage", "all").is_err());
        let submitted = model.body().unwrap();
        model.open(Some(submitted), true);
        assert_eq!(model.draft.as_ref().unwrap()["id"], 0);
        assert_eq!(model.draft.as_ref().unwrap()["expectedVersion"], 0);
        assert_eq!(model.direct().len(), 2);
    }
}
