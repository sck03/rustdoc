//! The opportunity editor keeps one draft across details, quote and history tabs.
use crate::{
    FormSection,
    form_model::{FormModel, Lookups},
    form_sections::{self, DisclosureState, Section},
};
use export_doc_engine::{contracts, generated_api::*};
use serde_json::{Value, json};

#[derive(Default)]
pub struct SalesModel {
    pub archived: bool,
    pub record: Option<Value>,
    pub form: Option<FormModel>,
    pub history: Vec<ApiSalesOpportunityHistoryDto>,
}
impl SalesModel {
    pub fn id(&self) -> i64 {
        self.record
            .as_ref()
            .and_then(|row| row["id"].as_i64())
            .unwrap_or(0)
    }
    pub fn version(&self) -> i64 {
        self.record
            .as_ref()
            .and_then(|row| row["versionNumber"].as_i64())
            .unwrap_or(0)
    }
    pub fn dirty(&self) -> bool {
        self.form
            .as_ref()
            .is_some_and(|form| form.value != form.baseline || !form.error.is_empty())
    }
    pub fn closed(&self) -> bool {
        self.record.as_ref().is_some_and(|row| {
            export_doc_domain::sales::closed(row["stage"].as_str().unwrap_or(""))
        })
    }
    pub fn open(&mut self, record: Option<Value>, customer_id: Option<i64>, lookups: Lookups) {
        let mut value = contracts::object(CREATE_SALES_OPPORTUNITY.id, true);
        value["currency"] = json!("USD");
        value["estimatedAmount"] = json!(0);
        value["probabilityPercent"] = json!(0);
        if let Some(record) = &record {
            value = contracts::overlay(value, record);
            value["expectedVersion"] = record["versionNumber"].clone();
        } else if let Some(id) = customer_id {
            value["crmCustomerId"] = json!(id);
        }
        value["changeNote"] = json!("");
        self.form = Some(FormModel::new(
            contracts::request(CREATE_SALES_OPPORTUNITY.id),
            value,
            lookups,
        ));
        self.record = record;
        self.archived = false;
        self.history.clear();
    }
    pub fn sections(&self, group: &str, state: &mut DisclosureState) -> Vec<FormSection> {
        let Some(form) = &self.form else {
            return vec![];
        };
        let sections = match group {
            "quote" => vec![Section::new(
                "sales-quote",
                "报价信息",
                &[
                    "quotationNo",
                    "estimatedAmount",
                    "currency",
                    "probabilityPercent",
                    "expectedCloseDate",
                ],
                true,
            )],
            _ => vec![
                Section::new(
                    "sales-details",
                    "商机资料",
                    &["crmCustomerId", "productId", "title", "nextAction"],
                    true,
                ),
                Section::new(
                    "sales-notes",
                    "商机备注与变更说明",
                    &["notes", "changeNote"],
                    false,
                ),
            ],
        };
        form_sections::build(form, &sections, state)
    }
}
