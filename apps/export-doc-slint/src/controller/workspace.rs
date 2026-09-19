use super::*;
use crate::{DataRow, Metric, PageTab, WorklistRow};
use serde_json::{Value, json};

impl Desktop {
    pub fn worklist_loaded(&mut self, value: Value) {
        let page = match serde_json::from_value::<WorklistPage>(value.clone()) {
            Ok(page) => page,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let previous = app
            .get_worklist_source_index()
            .checked_sub(1)
            .and_then(|index| self.worklist_sources.get(index as usize))
            .cloned();
        self.worklist_sources = page
            .sources
            .iter()
            .map(|source| source.key.clone())
            .collect();
        app.set_worklist_sources(model(
            std::iter::once("全部来源".into())
                .chain(
                    page.sources
                        .iter()
                        .map(|source| format!("{}（{}）", source.name, source.count).into()),
                )
                .collect(),
        ));
        app.set_worklist_source_index(
            previous
                .and_then(|key| {
                    self.worklist_sources
                        .iter()
                        .position(|source| *source == key)
                })
                .map(|index| index as i32 + 1)
                .unwrap_or(0),
        );
        app.set_worklist_rows(model(
            page.page
                .items
                .iter()
                .enumerate()
                .map(|(index, item)| WorklistRow {
                    index: index as i32,
                    title: item.title.clone().into(),
                    description: item.description.clone().into(),
                    overdue: item.is_overdue,
                    source: page
                        .sources
                        .iter()
                        .find(|source| source.key == item.source)
                        .map(|source| source.name.clone())
                        .unwrap_or_else(|| item.source.clone())
                        .into(),
                    due: item
                        .due_date
                        .clone()
                        .or_else(|| {
                            item.due_at
                                .as_deref()
                                .and_then(|date| chrono::DateTime::parse_from_rfc3339(date).ok())
                                .map(|date| date.format("%Y-%m-%d %H:%M %:z").to_string())
                        })
                        .unwrap_or_else(|| "未设截止日期".into())
                        .into(),
                })
                .collect(),
        ));
        app.set_list_page(page.page.page_number as i32);
        app.set_total_pages(page.page.total_pages as i32);
        app.set_total_records(page.page.total_count as i32);
        app.set_worklist_as_of(format!("业务日期：{}", page.business_date).into());
        self.workspace.payload = Some(value);
        self.worklist_items = page.page.items;
    }
    pub fn worklist_open(&mut self, index: i32) {
        if self.task.is_some() {
            return;
        }
        let Some(item) = self.worklist_items.get(index.max(0) as usize) else {
            return;
        };
        let target = match item.source.as_str() {
            "invoice-review" => "invoices",
            "customer-follow-up" => "crm-customers",
            "meeting-approval" | "meeting-collection" | "meeting-return" => "bookings",
            "supply-approval" | "supply-collection" | "supply-return" => "supply-requests",
            "probation-end" | "contract-end" => "people",
            _ => return,
        };
        self.pending_open = Some((
            if item.source == "customer-follow-up" {
                "crm-follow-ups"
            } else {
                target
            },
            item.record_id,
        ));
        self.navigate_now(target);
    }
    pub fn dashboard_loaded(&mut self, value: Value) {
        let data = match serde_json::from_value::<ApiDashboardResponse>(value) {
            Ok(data) => data,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        app.set_dashboard_period(data.period_label.clone().into());
        app.set_dashboard_summary(data.single_window_status_summary.into());
        app.set_metrics(model(vec![
            Metric {
                label: "本月出口额".into(),
                value: format!("{:.2}", data.monthly_export_amount).into(),
            },
            Metric {
                label: "本月预估利润".into(),
                value: format!("{:.2}", data.monthly_profit).into(),
            },
            Metric {
                label: "本月退税额".into(),
                value: format!("{:.2}", data.monthly_tax_refund).into(),
            },
            Metric {
                label: "待处理订单".into(),
                value: data.pending_count.to_string().into(),
            },
            Metric {
                label: "已出运".into(),
                value: data.shipped_count.to_string().into(),
            },
            Metric {
                label: format!("{}发票", data.period_label).into(),
                value: data.monthly_invoice_count.to_string().into(),
            },
        ]));
        let trend = |current: rust_decimal::Decimal, previous: rust_decimal::Decimal| {
            if previous.is_zero() {
                "上月无可比数据".to_owned()
            } else {
                format!(
                    "较上月 {:+.1}%",
                    (current - previous) / previous.abs() * rust_decimal::Decimal::from(100)
                )
            }
        };
        app.set_dashboard_details(model(vec![
            trend(
                data.monthly_export_amount,
                data.previous_monthly_export_amount,
            )
            .into(),
            trend(data.monthly_profit, data.previous_monthly_profit).into(),
            trend(data.monthly_tax_refund, data.previous_monthly_tax_refund).into(),
            format!("草稿 {} · 已核对 {}", data.draft_count, data.verified_count).into(),
            format!("已结汇 {}", data.completed_count).into(),
            format!("有效订单共 {}", data.total_active_count).into(),
        ]));
        app.set_dashboard_quick(model(
            [
                ("new-invoice", "新建发票", CREATE_INVOICE),
                ("query", "统计查询", LIST_QUERIED_INVOICES),
                ("hs-codes", "HS 查询", LIST_HS_CODES),
                ("jobs", "文件任务", LIST_JOBS),
            ]
            .into_iter()
            .filter(|(_, _, operation)| self.can(*operation))
            .map(|(key, label, _)| PageTab {
                key: key.into(),
                label: label.into(),
            })
            .collect(),
        ));
        app.set_dashboard_todos(model(
            data.todo_items
                .iter()
                .map(|todo| PageTab {
                    key: todo.reference_id.clone().into(),
                    label: format!("{}\n{}", todo.title, todo.description).into(),
                })
                .collect(),
        ));
        app.set_columns(model(
            ["发票号", "客户", "日期", "金额", "状态"]
                .map(Into::into)
                .to_vec(),
        ));
        app.set_records(model(
            data.recent_invoices
                .iter()
                .map(|invoice| DataRow {
                    id: invoice.id as i32,
                    cells: model(vec![
                        invoice.invoice_no.clone().into(),
                        invoice.customer_name_en.clone().into(),
                        invoice.invoice_date.clone().into(),
                        format!("{:.2}", invoice.total_amount).into(),
                        invoice.status_text.clone().into(),
                    ]),
                })
                .collect(),
        ));
        self.workspace.payload = Some(json!({"items":data.recent_invoices}));
    }
    pub fn dashboard_action(&mut self, key: &str) {
        if key == "new-invoice" {
            self.navigate_now("invoices");
            self.pending_new_invoice = true;
        } else if let Ok(id) = key.parse::<i64>() {
            self.request(GET_INVOICE, id, vec![], None, "invoice");
        } else {
            self.navigate(key);
        }
    }
}
