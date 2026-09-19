//! A selected business party owns its related lists; no relationship is inferred from UI row order.
use serde_json::Value;

pub struct BusinessModel {
    pub resource: &'static str,
    pub record: Value,
}
impl BusinessModel {
    pub fn id(&self) -> i64 {
        self.record["id"].as_i64().unwrap_or(0)
    }
    pub fn relation(&self) -> &'static str {
        if self.resource == "crm-customers" {
            "crmCustomerId"
        } else {
            "supplierCompanyId"
        }
    }
    pub fn tabs(&self) -> Vec<(&'static str, &'static str)> {
        if self.resource == "crm-customers" {
            vec![
                ("crm-customers", "客户资料"),
                ("crm-contacts", "联系人"),
                ("crm-follow-ups", "客户跟进"),
                ("opportunities", "商机与报价"),
            ]
        } else {
            vec![
                ("suppliers", "供应商资料"),
                ("supplier-contacts", "联系人"),
                ("supplier-products", "供应产品"),
                ("supplier-assessments", "评价记录"),
            ]
        }
    }
}
