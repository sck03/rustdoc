use super::*;
use crate::{Crm, DataRow, Metric};
use export_doc_engine::clock::BusinessClock;
use serde_json::{Value, json};

impl Desktop {
    pub fn prepare_business_form(&mut self) {
        let Some(operation) = self.form_operation else {
            return;
        };
        let Some(form) = &mut self.form else {
            return;
        };
        if [
            CREATE_CRM_FOLLOW_UP,
            UPDATE_CRM_FOLLOW_UP,
            TRANSFER_CRM_FOLLOW_UP,
        ]
        .contains(&operation)
        {
            form.lookups.insert(
                "type".into(),
                export_doc_domain::crm::FOLLOW_UP_TYPES
                    .iter()
                    .map(|value| (json!(value), (*value).into()))
                    .collect(),
            );
            if operation != TRANSFER_CRM_FOLLOW_UP {
                if self.form_id > 0 {
                    form.schema["properties"]["crmCustomerId"]["readOnly"] = json!(true);
                }
                if let Some(user) = &self.user {
                    match BusinessClock::new(&user.business_time_zone) {
                        Ok(clock) => {
                            for key in ["followedUpAt", "nextFollowUpAt"] {
                                let instant = form.value[key]
                                    .as_str()
                                    .filter(|value| !value.is_empty())
                                    .map(str::to_owned)
                                    .or_else(|| {
                                        (key == "followedUpAt")
                                            .then(|| chrono::Utc::now().to_rfc3339())
                                    });
                                if let Some(instant) = instant {
                                    match clock.local_input(&instant) {
                                        Ok(value) => form.value[key] = json!(value),
                                        Err(cause) => form.error = cause,
                                    }
                                }
                            }
                        }
                        Err(cause) => form.error = cause,
                    }
                }
                if form.value["type"].as_str().is_none_or(str::is_empty) {
                    form.value["type"] = json!("其他");
                }
            }
        }
        if [CREATE_SUPPLIER_ASSESSMENT, UPDATE_SUPPLIER_ASSESSMENT].contains(&operation) {
            for (key, values) in [
                (
                    "assessmentKind",
                    export_doc_domain::supplier::ASSESSMENT_KINDS,
                ),
                ("conclusion", export_doc_domain::supplier::CONCLUSIONS),
            ] {
                form.lookups.insert(
                    key.into(),
                    values
                        .iter()
                        .map(|value| (json!(value), (*value).into()))
                        .collect(),
                );
            }
            if self.form_id == 0 {
                form.value["assessmentDate"] = json!(
                    self.user
                        .as_ref()
                        .map(|user| user.business_date.as_str())
                        .unwrap_or("")
                );
                form.value["assessmentKind"] = json!("定期评价");
                form.value["conclusion"] = json!("合格");
                for key in [
                    "qualityScore",
                    "deliveryScore",
                    "serviceScore",
                    "priceScore",
                ] {
                    form.value[key] = json!(3);
                }
            }
        }
        form.baseline = form.value.clone();
        self.sync_form();
        self.refresh_follow_up_contacts();
    }
    pub fn refresh_follow_up_contacts(&mut self) {
        if !self.form_operation.is_some_and(|operation| {
            [
                CREATE_CRM_FOLLOW_UP,
                UPDATE_CRM_FOLLOW_UP,
                TRANSFER_CRM_FOLLOW_UP,
            ]
            .contains(&operation)
        }) {
            return;
        }
        if !self.can(QUERY_CRM_CONTACTS) {
            return;
        }
        let customer = self
            .form
            .as_ref()
            .and_then(|form| form.value["crmCustomerId"].as_i64())
            .unwrap_or(0);
        if customer > 0 {
            self.start(Work::FollowUpContacts {
                customer_id: customer,
            });
        }
    }
    pub fn follow_up_contacts_loaded(&mut self, value: Value) {
        if let Some(form) = &mut self.form {
            form.lookups.insert(
                "crmContactId".into(),
                value
                    .as_array()
                    .into_iter()
                    .flatten()
                    .map(|row| {
                        (
                            row["id"].clone(),
                            row["name"].as_str().unwrap_or("").to_owned(),
                        )
                    })
                    .collect(),
            );
            self.sync_form();
        }
    }
    pub fn crm_request_body(&self, mut body: Value) -> Result<Value, String> {
        if matches!(
            self.form_operation,
            Some(CREATE_CRM_FOLLOW_UP | UPDATE_CRM_FOLLOW_UP)
        ) {
            let clock =
                BusinessClock::new(&self.user.as_ref().ok_or("请重新登录。")?.business_time_zone)?;
            for key in ["followedUpAt", "nextFollowUpAt"] {
                body[key] = match body[key].as_str().filter(|value| !value.is_empty()) {
                    Some(value) => json!(clock.parse_local_input(value)?.to_rfc3339()),
                    None => Value::Null,
                };
            }
        }
        Ok(body)
    }
    pub fn sync_business_filters(&self) {
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<Crm>();
            let choices = if self.workspace.resource == "suppliers" {
                export_doc_domain::party::SUPPLIER_STATUSES
            } else {
                export_doc_domain::crm::CUSTOMER_STATUSES
            };
            view.set_statuses(model(
                std::iter::once("全部状态".into())
                    .chain(choices.iter().map(|choice| (*choice).into()))
                    .collect(),
            ));
            view.set_customers(model(
                std::iter::once("全部客户".into())
                    .chain(
                        self.lookups
                            .get("crmCustomerId")
                            .into_iter()
                            .flatten()
                            .map(|(_, name)| name.clone().into()),
                    )
                    .collect(),
            ));
        }
    }
    pub fn crm_dashboard_loaded(&mut self, value: Value) {
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<Crm>();
            let display = export_doc_engine::workspace::display;
            view.set_metrics(model(
                [
                    ("客户总数", "customerCount"),
                    ("联系人", "contactCount"),
                    ("待跟进", "pendingFollowUpCount"),
                    ("已逾期", "overdueFollowUpCount"),
                    ("未来七天", "dueNextSevenDaysCount"),
                ]
                .iter()
                .map(|(label, key)| Metric {
                    label: (*label).into(),
                    value: display(&value[*key]).into(),
                })
                .collect(),
            ));
            let rows = |key: &str, columns: &[&str]| {
                model(
                    value[key]
                        .as_array()
                        .into_iter()
                        .flatten()
                        .enumerate()
                        .map(|(index, row)| DataRow {
                            id: row["id"].as_i64().unwrap_or(index as i64 + 1) as i32,
                            cells: model(
                                columns
                                    .iter()
                                    .map(|key| display(&row[*key]).into())
                                    .collect(),
                            ),
                        })
                        .collect(),
                )
            };
            view.set_follow_ups(rows(
                "upcomingFollowUps",
                &[
                    "customerName",
                    "contactName",
                    "nextAction",
                    "nextFollowUpAt",
                ],
            ));
            view.set_stages(rows("opportunityStages", &["stage", "count"]));
            view.set_currencies(rows(
                "opportunityCurrencies",
                &["currency", "count", "estimatedAmount", "weightedAmount"],
            ));
            view.set_closings(rows(
                "upcomingOpportunityClosings",
                &[
                    "title",
                    "customerName",
                    "expectedCloseDate",
                    "currency",
                    "estimatedAmount",
                ],
            ));
            self.workspace.payload = Some(value);
        }
    }
    pub fn crm_action(&mut self, action: &str, id: i32) {
        if self.task.is_some() {
            return;
        }
        match action {
            "follow-up" => {
                self.pending_open = Some(("crm-follow-ups", id as i64));
                self.navigate_now("crm-customers");
            }
            "opportunity" => {
                self.pending_open = Some(("opportunities", id as i64));
                self.navigate_now("opportunities");
            }
            "filter" => {
                self.workspace.page = 1;
                self.refresh();
            }
            _ => {}
        }
    }
}
