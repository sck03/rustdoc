use super::*;
use crate::Query;
use serde_json::{Value, json};

impl Desktop {
    pub fn setup_query(&mut self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Query>();
        view.set_can_export(self.can(SAVE_QUERIED_INVOICES_TO_PATH));
        view.set_can_open(self.can(GET_INVOICE));
        for (key, label) in [("customerId", "全部客户"), ("exporterId", "全部出口商")] {
            let options = model(
                std::iter::once(label.into())
                    .chain(
                        self.lookups
                            .get(key)
                            .into_iter()
                            .flatten()
                            .map(|(_, label)| label.clone().into()),
                    )
                    .collect(),
            );
            if key == "customerId" {
                view.set_customers(options);
            } else {
                view.set_exporters(options);
            }
        }
        if view.get_start().is_empty() && view.get_end().is_empty() && self.query_filters.is_none()
        {
            self.reset_query_dates();
        }
    }
    fn reset_query_dates(&self) {
        use chrono::Datelike;
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Query>();
        if let Some(day) = self
            .user
            .as_ref()
            .and_then(|u| chrono::NaiveDate::parse_from_str(&u.business_date, "%Y-%m-%d").ok())
        {
            view.set_start(day.with_day(1).unwrap().to_string().into());
            view.set_end(day.to_string().into());
        }
    }
    fn query_input(&self) -> Result<Value, String> {
        let ui = self.ui.upgrade().ok_or("窗口已关闭。")?;
        let view = ui.global::<Query>();
        let lookup = |key: &str, index: i32| {
            index
                .checked_sub(1)
                .and_then(|i| self.lookups.get(key)?.get(i as usize))
                .map(|(value, _)| value.clone())
                .unwrap_or(Value::Null)
        };
        let mut body = json!({"customerId":lookup("customerId",view.get_customer_index()),"exporterId":lookup("exporterId",view.get_exporter_index()),
            "keyword":view.get_keyword().to_string(),"contractNo":view.get_contract().to_string(),"styleName":view.get_style_name().to_string(),"styleNo":view.get_style_no().to_string(),
            "invoiceType":(["","实际数据","报关数据"].get(view.get_type_index() as usize).unwrap_or(&"")),
            "transportMode":(["","BY SEA","BY AIR","BY TRAIN","BY DHL","BY FedEx"].get(view.get_transport_index() as usize).unwrap_or(&""))});
        for (key, value) in [
            ("startDate", view.get_start()),
            ("endDateExclusive", view.get_end()),
        ] {
            if value.trim().is_empty() {
                continue;
            }
            if !export_doc_domain::invoice::valid_date(value.trim()) {
                return Err("查询日期须为有效的 YYYY-MM-DD。".into());
            }
            let date = chrono::NaiveDate::parse_from_str(value.trim(), "%Y-%m-%d")
                .map_err(|e| e.to_string())?;
            let date = if key == "endDateExclusive" {
                date.succ_opt()
                    .filter(|d| d.to_string().len() == 10)
                    .ok_or("结束日期超出范围。")?
            } else {
                date
            };
            body[key] = json!(date.to_string());
        }
        export_doc_domain::invoice_query::Criteria::new(
            serde_json::from_value(body.clone()).map_err(|e| e.to_string())?,
        )?;
        Ok(body)
    }
    pub fn refresh_query(&mut self) {
        let filter = match self
            .query_filters
            .clone()
            .map(Ok)
            .unwrap_or_else(|| self.query_input())
        {
            Ok(v) => v,
            Err(e) => {
                self.error(e);
                return;
            }
        };
        self.query_filters = Some(filter.clone());
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        app.set_selected_id(0);
        app.set_actions(model(vec![]));
        self.workspace.selected = None;
        let page_size = [20, 50, 100, 200]
            .get(ui.global::<Query>().get_page_size_index() as usize)
            .copied()
            .unwrap_or(50);
        let mut query = vec![
            ("pageNumber", self.workspace.page.to_string()),
            ("pageSize", page_size.to_string()),
        ];
        for key in [
            "startDate",
            "endDateExclusive",
            "customerId",
            "exporterId",
            "keyword",
            "contractNo",
            "styleName",
            "styleNo",
            "invoiceType",
            "transportMode",
        ] {
            if let Some(value) = filter.get(key).filter(|v| !v.is_null()) {
                query.push((
                    key,
                    value
                        .as_str()
                        .map(str::to_owned)
                        .unwrap_or_else(|| value.to_string()),
                ));
            }
        }
        self.request(LIST_QUERIED_INVOICES, 0, query, None, "list:query");
    }
    pub fn query_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<Query>();
        if action == "reset" {
            view.set_keyword("".into());
            view.set_contract("".into());
            view.set_style_name("".into());
            view.set_style_no("".into());
            view.set_customer_index(0);
            view.set_exporter_index(0);
            view.set_type_index(0);
            view.set_transport_index(0);
            self.reset_query_dates();
        }
        if matches!(action, "search" | "reset") {
            match self.query_input() {
                Ok(v) => self.query_filters = Some(v),
                Err(e) => {
                    self.error(e);
                    return;
                }
            }
            self.workspace.page = 1;
        }
        if action == "page-size" {
            self.workspace.page = 1;
        }
        if action == "export" {
            if !self.can(SAVE_QUERIED_INVOICES_TO_PATH) {
                return;
            }
            let Some(mut body) = self.query_filters.clone() else {
                return;
            };
            let Some(destination) =
                self.platform
                    .choose_destination(ui.window(), "查询结果.xlsx", &["xlsx"])
            else {
                return;
            };
            body["destinationPath"] = json!(destination);
            self.start(Work::FileJob {
                operation: SAVE_QUERIED_INVOICES_TO_PATH,
                parameters: vec![],
                body,
                destination,
            });
            return;
        }
        self.refresh_query();
    }
}
