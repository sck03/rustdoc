use super::*;
use crate::{DataRow, Metric, SupplierOverview};
use export_doc_engine::workspace::display;
use serde_json::Value;
impl Desktop {
    pub fn supplier_overview_loaded(&mut self, value: Value) {
        if let Some(ui) = self.ui.upgrade() {
            let view = ui.global::<SupplierOverview>();
            view.set_metrics(model(
                [
                    ("供应商总数", "totalSuppliers"),
                    ("未评价", "unassessedSuppliers"),
                    ("优先合作", "preferredCount"),
                    ("需要观察", "watchCount"),
                    ("暂停合作", "pausedCount"),
                ]
                .iter()
                .map(|(label, key)| Metric {
                    label: (*label).into(),
                    value: display(&value[*key]).into(),
                })
                .collect(),
            ));
            view.set_summary(format!("已评价 {} 家，合格 {} 家。四项平均分：质量 {} · 交期 {} · 服务 {} · 价格 {}。统计使用每家供应商最近一条已确认评价。",value["assessedSuppliers"],value["qualifiedCount"],value["averageQualityScore"],value["averageDeliveryScore"],value["averageServiceScore"],value["averagePriceScore"]).into());
            view.set_rows(model(
                value["items"]
                    .as_array()
                    .into_iter()
                    .flatten()
                    .map(|row| DataRow {
                        id: row["supplierCompanyId"].as_i64().unwrap_or(0) as i32,
                        cells: model(
                            [
                                "supplierName",
                                "supplierStatus",
                                "category",
                                "assessmentCount",
                                "latestAssessmentDate",
                                "averageScore",
                                "conclusion",
                            ]
                            .iter()
                            .map(|key| display(&row[*key]).into())
                            .collect(),
                        ),
                    })
                    .collect(),
            ));
            ui.global::<App>().set_page("supplier-overview".into());
            self.workspace.payload = Some(value);
        }
    }
}
