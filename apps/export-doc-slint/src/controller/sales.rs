use super::*;
use crate::{Sales, SalesHistoryRow};
use export_doc_domain::sales;
use serde_json::{Value, json};

impl Desktop {
    fn sales_can_quote(&self) -> bool {
        let action = if self.sales.id() > 0 {
            "edit"
        } else {
            "create"
        };
        self.user.as_ref().is_some_and(|user| {
            user.capabilities.permissions.iter().any(|grant| {
                grant.resource_key == "sales.quotes"
                    && grant.action == action
                    && grant.data_scope != "none"
            })
        })
    }
    fn sales_can_edit(&self) -> bool {
        !self.sales.archived
            && !self.sales.closed()
            && self.can(if self.sales.id() > 0 {
                UPDATE_SALES_OPPORTUNITY
            } else {
                CREATE_SALES_OPPORTUNITY
            })
    }
    pub fn setup_sales(&mut self) {
        self.sales = Default::default();
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<Sales>();
            view.set_view("directory".into());
            view.set_stage_filter(0);
            view.set_stages(model(
                std::iter::once("全部阶段".into())
                    .chain(sales::STAGES.iter().map(|stage| (*stage).into()))
                    .collect(),
            ));
        }
        self.sync_sales();
    }
    pub fn sync_sales(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Sales>();
        view.set_saved(self.sales.id() > 0);
        view.set_can_edit(self.sales_can_edit());
        view.set_can_quote(self.sales_can_quote());
        view.set_can_transition(!self.sales.archived && self.can(TRANSITION_SALES_OPPORTUNITY));
        view.set_can_archive(!self.sales.archived && self.can(ARCHIVE_SALES_OPPORTUNITY));
        view.set_dirty(self.sales.dirty());
        view.set_title(
            self.sales
                .record
                .as_ref()
                .and_then(|row| row["title"].as_str())
                .unwrap_or("新建商机")
                .into(),
        );
        view.set_stage(
            self.sales
                .record
                .as_ref()
                .and_then(|row| row["stage"].as_str())
                .unwrap_or("线索")
                .into(),
        );
        view.set_version(format!("版本 {}", self.sales.version()).into());
        view.set_form_error(
            self.sales
                .form
                .as_ref()
                .map(|form| form.error.as_str())
                .unwrap_or("")
                .into(),
        );
        view.set_invalid_field(
            self.sales
                .form
                .as_ref()
                .map(|form| form.invalid_field.as_str())
                .unwrap_or("")
                .into(),
        );
        view.set_details(model(
            self.sales.sections("details", &mut self.disclosure_state),
        ));
        view.set_quote(model(
            self.sales.sections("quote", &mut self.disclosure_state),
        ));
        view.set_next_stages(model(
            self.sales
                .record
                .as_ref()
                .and_then(|row| row["allowedNextStages"].as_array())
                .map(|stages| {
                    stages
                        .iter()
                        .filter_map(|stage| stage.as_str().map(Into::into))
                        .collect()
                })
                .unwrap_or_default(),
        ));
        view.set_history(model(
            self.sales
                .history
                .iter()
                .map(|row| SalesHistoryRow {
                    version: row.version_number as i32,
                    kind: row.change_type.clone().into(),
                    stage: row.stage.clone().into(),
                    quote: if row.quotation_no.is_empty() {
                        "未填写".into()
                    } else {
                        row.quotation_no.clone().into()
                    },
                    amount: format!(
                        "{} {} · 成交概率 {}%",
                        row.currency, row.estimated_amount, row.probability_percent
                    )
                    .into(),
                    expected: format!(
                        "预计成交日期：{}",
                        row.expected_close_date.as_deref().unwrap_or("未填写")
                    )
                    .into(),
                    changed: format!("{} · {}", row.changed_by, row.created_at).into(),
                    note: row.change_note.clone().into(),
                })
                .collect(),
        ));
    }
    pub fn sales_edit(&mut self, key: &str, value: &str, choice: Option<i32>) {
        if self.task.is_some()
            || !self.sales_can_edit()
            || (sales::QUOTE_FIELDS.contains(&key) && !self.sales_can_quote())
        {
            return;
        }
        let Some(form) = &mut self.sales.form else {
            return;
        };
        let result = match choice {
            Some(index) => form.choose(key, index.max(0) as usize),
            None => form.edit(key, value),
        };
        if let Err(cause) = result {
            form.error = cause;
        }
        self.sync_sales();
    }
    pub fn sales_action(&mut self, action: &str, value: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        if action == "toggle" {
            let sections = [
                ui.global::<Sales>().get_details(),
                ui.global::<Sales>().get_quote(),
            ];
            for sections in sections {
                for section in sections.iter().filter(|section| section.key == value) {
                    self.disclosure_state
                        .insert(value.into(), !section.expanded);
                }
            }
            self.sync_sales();
            return;
        }
        if action == "history" && self.sales.id() > 0 {
            ui.global::<Sales>().set_view("history".into());
            self.request(
                LIST_SALES_OPPORTUNITY_HISTORY,
                self.sales.id(),
                vec![],
                None,
                "sales:history",
            );
            return;
        }
        if action == "editor" && self.sales.form.is_some() {
            ui.global::<Sales>().set_view("editor".into());
            return;
        }
        if action == "save" {
            if !self.sales_can_edit() {
                return;
            }
            let id = self.sales.id();
            let Some(form) = &mut self.sales.form else {
                return;
            };
            if let Err(cause) = form.validate() {
                form.error = cause;
                self.sync_sales();
                return;
            }
            let body = form.value.clone();
            self.request(
                if id > 0 {
                    UPDATE_SALES_OPPORTUNITY
                } else {
                    CREATE_SALES_OPPORTUNITY
                },
                id,
                vec![],
                Some(body),
                "sales:saved",
            );
            return;
        }
        if action == "transition" || action == "archive" || self.sales.dirty() {
            let message = if action == "transition" {
                format!("确认将商机流转到“{value}”？未保存的资料修改将被放弃。")
            } else if action == "archive" {
                "确认归档此商机？它将从当前清单隐藏，报价及阶段历史仍保留。".into()
            } else {
                "当前商机有未保存修改，确认放弃修改并继续？".into()
            };
            self.confirm(Pending::SalesAction(action.into(), value.into()), &message);
            return;
        }
        self.sales_confirmed(action, value);
    }
    pub fn sales_confirmed(&mut self, action: &str, value: &str) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Sales>();
        match action {
            "open" | "reload" => {
                let id = if action == "open" {
                    value.parse().unwrap_or(0)
                } else {
                    self.sales.id()
                };
                if id > 0 {
                    view.set_view("editor".into());
                    self.request(GET_SALES_OPPORTUNITY, id, vec![], None, "sales:record");
                }
            }
            "new" | "editor" => {
                if !self.can(CREATE_SALES_OPPORTUNITY) {
                    return;
                }
                self.sales.open(
                    None,
                    self.business
                        .as_ref()
                        .filter(|row| row.resource == "crm-customers")
                        .map(|row| row.id()),
                    self.lookups.clone(),
                );
                ui.global::<App>().set_page("sales".into());
                view.set_view("editor".into());
                self.sync_sales();
            }
            "directory" => {
                self.sales = Default::default();
                view.set_view("directory".into());
                ui.global::<App>().set_page(
                    if self.business.is_some() {
                        "business"
                    } else {
                        "sales"
                    }
                    .into(),
                );
                self.sync_sales();
                self.refresh();
            }
            "customer" => {
                if let Some(id) = self
                    .sales
                    .record
                    .as_ref()
                    .and_then(|row| row["crmCustomerId"].as_i64())
                {
                    self.pending_open = Some(("crm-customers", id));
                    self.navigate_now("crm-customers");
                }
            }
            "archive" if self.can(ARCHIVE_SALES_OPPORTUNITY) => self.request(
                ARCHIVE_SALES_OPPORTUNITY,
                self.sales.id(),
                vec![],
                Some(json!({"expectedVersion":self.sales.version()})),
                "sales:archived",
            ),
            "transition" if self.can(TRANSITION_SALES_OPPORTUNITY) => {
                if !view.get_next_stages().iter().any(|stage| stage == value) {
                    self.error("该阶段不允许此项流转。");
                    return;
                }
                self.request(TRANSITION_SALES_OPPORTUNITY, self.sales.id(), vec![], Some(json!({"expectedVersion":self.sales.version(), "nextStage":value, "changeNote":view.get_transition_note().as_str()})), "sales:saved");
            }
            _ => {}
        }
    }
    pub fn sales_loaded(&mut self, reply: &str, value: Value) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Sales>();
        match reply {
            "sales:record" | "sales:saved" => {
                self.sales.open(Some(value), None, self.lookups.clone());
                ui.global::<App>().set_page("sales".into());
                if reply == "sales:record" {
                    view.set_view("editor".into());
                }
                view.set_transition_note("".into());
                self.sync_sales();
                self.request(
                    LIST_SALES_OPPORTUNITY_HISTORY,
                    self.sales.id(),
                    vec![],
                    None,
                    "sales:history",
                );
            }
            "sales:history" => match serde_json::from_value(value) {
                Ok(rows) => {
                    self.sales.history = rows;
                    self.sync_sales();
                }
                Err(cause) => self.error(format!("商机历史格式无效：{cause}")),
            },
            "sales:archived" => {
                self.sales.archived = true;
                view.set_view("history".into());
                view.set_can_edit(false);
                view.set_can_transition(false);
                view.set_can_archive(false);
                self.sales.form = None;
                self.request(
                    LIST_SALES_OPPORTUNITY_HISTORY,
                    self.sales.id(),
                    vec![],
                    None,
                    "sales:archived-history",
                );
            }
            "sales:archived-history" => {
                self.sales_loaded("sales:history", value);
                view.set_can_edit(false);
                view.set_can_transition(false);
                view.set_can_archive(false);
                self.status("商机已归档，历史仍可查看");
            }
            _ => {}
        }
    }
}
