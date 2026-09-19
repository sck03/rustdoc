use super::*;
use crate::{
    Access, PageTab, PermissionRow,
    access_model::{self, scope_label},
};
use export_doc_domain::permissions;
use serde_json::{Value, json};

impl Desktop {
    pub fn access_loaded(&mut self, value: Value) {
        self.access.templates = value["templates"].as_array().cloned().unwrap_or_default();
        self.access.templates.sort_by(|a, b| {
            b["isSystem"]
                .as_bool()
                .cmp(&a["isSystem"].as_bool())
                .then_with(|| access_model::text(a, "name").cmp(access_model::text(b, "name")))
        });
        if self.access.draft.is_none() {
            if let Some(first) = self.access.templates.first().cloned() {
                self.access.open(Some(first), false);
            }
        }
        self.sync_access();
    }
    pub fn access_action(&mut self, action: &str, key: &str) {
        if self.task.is_some() {
            return;
        }
        if matches!(action, "select" | "new" | "copy" | "refresh") && self.access.dirty() {
            self.error("权限方案有未保存修改，请先保存或取消修改。");
            return;
        }
        match action {
            "select" => {
                if let Some(record) = self
                    .access
                    .templates
                    .iter()
                    .find(|record| {
                        record["id"].as_i64().map(|id| id.to_string()).as_deref() == Some(key)
                    })
                    .cloned()
                {
                    self.access.open(Some(record), false);
                }
            }
            "new" => self.access.open(None, false),
            "copy" => self.access.open(self.access.draft.clone(), true),
            "cancel" => {
                self.access.draft = self.access.baseline.clone();
            }
            "refresh" => {
                self.request(LIST_PERMISSION_TEMPLATES, 0, vec![], None, "access-catalog");
                return;
            }
            "save" => match self.access.body() {
                Ok(body) => {
                    let id = body["id"].as_i64().unwrap_or(0);
                    self.request(
                        if id > 0 {
                            UPDATE_PERMISSION_TEMPLATE
                        } else {
                            CREATE_PERMISSION_TEMPLATE
                        },
                        id,
                        vec![],
                        Some(body),
                        "access-saved",
                    );
                    return;
                }
                Err(error) => self.error(error),
            },
            "delete" => {
                if let Some(draft) = &self.access.draft {
                    if draft["isSystem"] == true || draft["id"].as_i64().unwrap_or(0) <= 0 {
                        return;
                    }
                    self.confirm(
                        Pending::AccessDelete(
                            draft["id"].as_i64().unwrap_or(0),
                            draft["versionNumber"].as_i64().unwrap_or(0),
                        ),
                        "确认删除此权限方案？仍有账号使用的方案会保留。",
                    );
                }
            }
            "search" => self.access.search = key.into(),
            "workspace" => self.access.workspace = key.into(),
            _ => {
                if let Some(level) = action.strip_prefix("preset:") {
                    let scope = self
                        .ui
                        .upgrade()
                        .map(|ui| match ui.global::<Access>().get_access_scope() {
                            0 => "own",
                            1 => "department",
                            2 => "company",
                            _ => "all",
                        })
                        .unwrap_or("own");
                    if let Err(error) = self.access.preset(key, level, scope) {
                        self.error(error);
                    }
                }
            }
        }
        self.sync_access();
    }
    pub fn access_field(&mut self, key: &str, value: &str) {
        self.access.edit(key, value);
    }
    pub fn access_grant(&mut self, key: &str, index: i32) {
        let Some((resource, action)) = key.split_once('|') else {
            return;
        };
        let scope = ["", "own", "department", "company", "all"]
            .get(index as usize)
            .copied()
            .unwrap_or("");
        if let Err(error) = self.access.grant(resource, action, scope) {
            self.error(error);
        }
        self.sync_access();
    }
    pub fn sync_access(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<Access>();
        app.set_access_search(self.access.search.clone().into());
        app.set_access_workspace_index(
            ["", "document", "sales", "office", "common"]
                .iter()
                .position(|value| *value == self.access.workspace)
                .unwrap_or(0) as i32,
        );
        app.set_access_templates(model(
            self.access
                .templates
                .iter()
                .map(|record| PageTab {
                    key: record["id"].to_string().into(),
                    label: format!(
                        "{}{}{}",
                        access_model::text(record, "name"),
                        if record["isSystem"] == true {
                            " · 内置"
                        } else {
                            ""
                        },
                        if record["isActive"] != true {
                            " · 已停用"
                        } else {
                            ""
                        }
                    )
                    .into(),
                })
                .collect(),
        ));
        let draft = self.access.draft.clone().unwrap_or(json!({}));
        app.set_access_id(draft["id"].as_i64().unwrap_or(0) as i32);
        app.set_access_code(access_model::text(&draft, "code").into());
        app.set_access_name(access_model::text(&draft, "name").into());
        app.set_access_description(access_model::text(&draft, "description").into());
        app.set_access_active(draft["isActive"] == true);
        app.set_access_system(draft["isSystem"] == true);
        app.set_access_read_only(self.access.read_only());
        let direct = self.access.direct();
        let effective =
            permissions::effective_details(&access_model::grants(&draft)).unwrap_or_default();
        let needle = self.access.search.to_lowercase();
        let mut rows = vec![];
        for resource in &permissions::catalog().resources {
            if resource.is_technical
                || (!self.access.workspace.is_empty()
                    && resource.workspace != self.access.workspace)
            {
                continue;
            }
            let matches_resource = needle.is_empty()
                || format!("{} {} {}", resource.name, resource.group, resource.key)
                    .to_lowercase()
                    .contains(&needle);
            let actions: Vec<_> = resource
                .actions
                .iter()
                .filter(|action| {
                    matches_resource
                        || format!("{} {}", action.name, action.description)
                            .to_lowercase()
                            .contains(&needle)
                })
                .collect();
            if actions.is_empty() {
                continue;
            }
            rows.push(PermissionRow {
                key: resource.key.clone().into(),
                label: resource.name.clone().into(),
                group: true,
                detail: resource.group.clone().into(),
                scope: 0,
                supports_scope: resource.supports_data_scope,
                effective: "".into(),
            });
            for action in actions {
                let scope = direct
                    .get(&(resource.key.clone(), action.key.clone()))
                    .map(String::as_str)
                    .unwrap_or("");
                let inherited = effective.iter().find(|grant| {
                    grant.grant.resource_key == resource.key && grant.grant.action == action.key
                });
                let explanation = inherited
                    .map(|grant| {
                        format!(
                            "{} · {}",
                            scope_label(&grant.grant.data_scope),
                            if grant.source == "dependency" {
                                format!(
                                    "由{}继承",
                                    permissions::resource(&grant.source_resource_key)
                                        .map(|resource| resource.name.as_str())
                                        .unwrap_or(&grant.source_resource_key)
                                )
                            } else {
                                "直接授权".into()
                            }
                        )
                    })
                    .unwrap_or_else(|| "未开放".into());
                rows.push(PermissionRow {
                    key: format!("{}|{}", resource.key, action.key).into(),
                    label: action.name.clone().into(),
                    detail: action.description.clone().into(),
                    group: false,
                    scope: ["", "own", "department", "company", "all"]
                        .iter()
                        .position(|value| *value == scope)
                        .unwrap_or(0) as i32,
                    supports_scope: resource.supports_data_scope,
                    effective: explanation.into(),
                });
            }
        }
        let technical = effective
            .iter()
            .filter(|grant| {
                permissions::resource(&grant.grant.resource_key)
                    .is_some_and(|resource| resource.is_technical)
            })
            .map(|grant| {
                format!(
                    "{} · {} · {}",
                    permissions::resource(&grant.grant.resource_key)
                        .map(|resource| resource.name.as_str())
                        .unwrap_or("技术依赖"),
                    grant.grant.action,
                    scope_label(&grant.grant.data_scope)
                )
            })
            .collect::<Vec<_>>();
        app.set_access_summary(
            format!(
                "{} 项直接授权 · {} 项最终权限。修改方案后，使用该方案的登录会话将失效。",
                direct.len(),
                effective.len()
            )
            .into(),
        );
        app.set_access_dependencies(technical.join("\n").into());
        app.set_access_rows(model(rows));
    }
}
