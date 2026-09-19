use super::*;
use crate::{DataRow, Metric, PageTab};
use export_doc_engine::{engine::catalog, workspace};

impl Desktop {
    pub fn sync_records(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let mut items = self.workspace.items();
        let columns: Vec<(String, String)> =
            if let Some(resource) = catalog::resource(self.workspace.resource) {
                resource
                    .columns
                    .iter()
                    .map(|(key, label)| ((*key).into(), (*label).into()))
                    .collect()
            } else {
                let definition: &[(&str, &str)] = match self.workspace.resource {
                    "dashboard" => &[
                        ("invoiceNo", "发票号"),
                        ("customerNameEN", "客户"),
                        ("invoiceDate", "日期"),
                        ("totalAmount", "金额"),
                        ("status", "状态"),
                    ],
                    "query" => export_doc_domain::invoice_query::COLUMNS,
                    "directory" => &[
                        ("fullName", "姓名"),
                        ("departmentName", "部门"),
                        ("jobTitle", "岗位"),
                        ("workPhone", "工作电话"),
                        ("workEmail", "工作邮箱"),
                    ],
                    "jobs" => &[
                        ("title", "任务"),
                        ("statusText", "状态"),
                        ("progressPercent", "进度 %"),
                        ("createdAt", "创建时间"),
                    ],
                    "audit" => &[
                        ("timestamp", "时间"),
                        ("entityName", "实体"),
                        ("entityId", "业务编号"),
                        ("action", "操作"),
                        ("userId", "操作人"),
                    ],
                    "backup" => &[
                        ("fileName", "备份文件"),
                        ("createdAt", "创建时间"),
                        ("size", "大小"),
                    ],
                    "attachments" => &[
                        ("name", "资料名称"),
                        ("invoiceNo", "发票号"),
                        ("categoryName", "分类"),
                        ("status", "状态"),
                    ],
                    _ => &[
                        ("title", "事项"),
                        ("source", "来源"),
                        ("statusText", "状态"),
                        ("dueAt", "到期时间"),
                    ],
                };
                definition
                    .iter()
                    .map(|(k, l)| ((*k).into(), (*l).into()))
                    .collect()
            };
        if let Some(payload) = &self.workspace.payload {
            let payload = payload
                .get("page")
                .filter(|value| value.is_object())
                .unwrap_or(payload);
            if self.workspace.resource == "dashboard" {
                items = payload["recentInvoices"]
                    .as_array()
                    .cloned()
                    .unwrap_or_default();
                app.set_metrics(model(
                    [
                        ("本月出口额", "monthlyExportAmount"),
                        ("本月发票", "monthlyInvoiceCount"),
                        ("待核对", "draftCount"),
                        ("已出运", "shippedCount"),
                    ]
                    .into_iter()
                    .map(|(label, key)| Metric {
                        label: label.into(),
                        value: workspace::display(&payload[key]).into(),
                    })
                    .collect(),
                ));
            }
            app.set_list_page(
                payload["pageNumber"]
                    .as_i64()
                    .unwrap_or(self.workspace.page) as i32,
            );
            app.set_total_pages(payload["totalPages"].as_i64().unwrap_or(1) as i32);
            app.set_total_records(
                payload["totalCount"].as_i64().unwrap_or(items.len() as i64) as i32
            );
        }
        app.set_columns(model(
            columns
                .iter()
                .map(|(_, label)| label.clone().into())
                .collect(),
        ));
        app.set_records(model(
            items
                .iter()
                .enumerate()
                .map(|(index, item)| DataRow {
                    id: item["id"]
                        .as_i64()
                        .or_else(|| item["recordId"].as_i64())
                        .unwrap_or(index as i64 + 1) as i32,
                    cells: model(
                        columns
                            .iter()
                            .map(|(key, _)| workspace::display(&item[key]).into())
                            .collect(),
                    ),
                })
                .collect(),
        ));
        self.status(format!("已读取 {} 条记录", items.len()));
    }
    pub fn select_record(&mut self, id: i64) {
        self.workspace.selected = self
            .workspace
            .items()
            .into_iter()
            .enumerate()
            .find(|(index, item)| {
                item["id"]
                    .as_i64()
                    .or_else(|| item["recordId"].as_i64())
                    .unwrap_or(*index as i64 + 1)
                    == id
            })
            .map(|(_, item)| item);
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_selected_id(id as i32);
            if self.workspace.resource == "audit" {
                if let Some(record) = &self.workspace.selected {
                    self.audit_select(record);
                }
            }
            if self.workspace.resource == "jobs" {
                if let Some(job) = &self.workspace.selected {
                    app.set_job_can_cancel(job["canCancel"] == true);
                    app.set_job_can_delete(matches!(
                        job["status"].as_str(),
                        Some("Succeeded" | "Failed" | "Canceled")
                    ));
                    app.set_job_can_retry(job["canRetry"] == true && self.can(RETRY_JOB));
                    app.set_job_detail(
                        format!(
                            "{}\n{}\n完成：{}  ·  输出：{}",
                            workspace::display(&job["title"]),
                            if job["errorMessage"]
                                .as_str()
                                .is_some_and(|value| !value.is_empty())
                            {
                                workspace::display(&job["errorMessage"])
                            } else {
                                workspace::display(&job["detailText"])
                            },
                            workspace::display(&job["completedAt"]),
                            workspace::display(&job["outputPath"])
                        )
                        .into(),
                    );
                    app.set_job_can_download(
                        job["status"] == "Succeeded"
                            && job["outputPath"]
                                .as_str()
                                .is_some_and(|path| !path.is_empty()),
                    );
                }
            }
            app.set_actions(model(
                self.workspace
                    .selected
                    .as_ref()
                    .map(|record| {
                        workspace::actions(self.workspace.resource, record)
                            .into_iter()
                            .filter(|(op, _)| self.can(*op))
                            .map(|(op, label)| PageTab {
                                key: op.id.into(),
                                label: label.into(),
                            })
                            .collect()
                    })
                    .unwrap_or_default(),
            ));
        }
    }
    pub fn new_record(&mut self) {
        if !catalog::resource(self.workspace.resource)
            .is_some_and(|resource| self.can(resource.create))
        {
            self.error("当前账号没有新建权限。");
            return;
        }
        match self.workspace.resource {
            "opportunities" => self.sales_action("new", ""),
            "people" => self.request(GET_CURRENT_USER, 0, vec![], None, "new-personnel-user"),
            "payments" => self.request(GET_CURRENT_USER, 0, vec![], None, "new-payment-user"),
            "invoices" => self.request(GET_CURRENT_USER, 0, vec![], None, "new-invoice-user"),
            "report-templates" => self.open_designer(None),
            _ => self.edit_record(None),
        }
    }
    pub fn open_record(&mut self, id: i64) {
        if self.workspace.resource == "query" {
            self.pending_open = Some(("invoices", id));
            self.navigate_now("invoices");
            return;
        }
        self.select_record(id);
        if self.workspace.resource == "crm-follow-ups" {
            self.request(
                QUERY_CRM_FOLLOW_UPS,
                0,
                vec![
                    ("followUpId", id.to_string()),
                    ("includeCompleted", "true".into()),
                ],
                None,
                "followup-record",
            );
            return;
        }
        if self.workspace.resource == "supplier-overview" {
            self.pending_open = Some(("suppliers", id));
            self.navigate_now("suppliers");
            return;
        }
        if self.workspace.resource == "opportunities" {
            self.sales_action("open", &id.to_string());
            return;
        }
        if self.workspace.resource == "people" {
            self.request(GET_PERSONNEL, id, vec![], None, "personnel-record");
            return;
        }
        if self.workspace.resource == "directory" {
            return;
        }
        if matches!(self.workspace.resource, "crm-customers" | "suppliers") {
            let operation = catalog::resource(self.workspace.resource)
                .unwrap()
                .get
                .unwrap();
            self.request(operation, id, vec![], None, "business-detail");
            return;
        }
        if self.workspace.resource == "worklist" {
            let source = self
                .workspace
                .selected
                .as_ref()
                .and_then(|item| item["source"].as_str())
                .unwrap_or("");
            let target = match source {
                "invoice-review" => "invoices",
                "customer-follow-up" => "crm-customers",
                "meeting-approval" | "meeting-collection" | "meeting-return" => "bookings",
                "supply-approval" | "supply-collection" | "supply-return" => "supply-requests",
                "probation-end" | "contract-end" => "people",
                _ => return,
            };
            self.pending_open = Some((
                if source == "customer-follow-up" {
                    "crm-follow-ups"
                } else {
                    target
                },
                id,
            ));
            self.navigate_now(target);
            return;
        }
        if self.workspace.resource == "invoices"
            || self.workspace.resource == "query"
            || self.workspace.resource == "dashboard"
        {
            self.request(GET_INVOICE, id, vec![], None, "invoice");
            return;
        }
        if self.workspace.resource == "report-templates" {
            self.request(GET_USER_REPORT_TEMPLATE, id, vec![], None, "template");
            return;
        }
        if self.workspace.resource == "payments" {
            self.request(GET_PAYMENT, id, vec![], None, "payment");
            return;
        }
        let Some(resource) = catalog::resource(self.workspace.resource) else {
            return;
        };
        if let Some(get) = resource.get {
            self.request(get, id, vec![], None, format!("edit:{}", resource.key));
        } else if let Some(record) = self.workspace.selected.clone() {
            self.edit_record(Some(record));
        } else {
            self.error("记录已变化，请刷新目录后重新选择。");
        }
    }
}
