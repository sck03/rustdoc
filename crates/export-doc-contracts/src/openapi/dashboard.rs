use serde_json::{Value, json};
#[cfg(test)]
pub(super) const FIELDS: &[(&str, &[&str])] = &[
    (
        "ApiDashboardResponse",
        &["exportAmountCurrency", "exportAmountsByCurrency"],
    ),
    ("ApiDashboardRecentInvoiceDto", &["currency"]),
];
pub(super) fn extend(document: &mut Value) {
    let schemas = &mut document["components"]["schemas"];
    schemas["ApiDashboardRecentInvoiceDto"]["properties"]["currency"] = json!({"type":"string"});
    let amount = schemas["ApiDashboardResponse"]["properties"]["monthlyExportAmount"].clone();
    schemas["ApiDashboardCurrencyAmount"] = json!({"type":"object","required":["currency","monthlyAmount","previousMonthlyAmount"],"properties":{
        "currency":{"type":"string"},"monthlyAmount":amount,"previousMonthlyAmount":amount
    }});
    let properties = &mut schemas["ApiDashboardResponse"]["properties"];
    properties["exportAmountCurrency"] = json!({"type":"string","description":"Currency of the scalar export amounts. Empty for no invoices or mixed currencies; scalar amounts are then zero. Use exportAmountsByCurrency."});
    properties["exportAmountsByCurrency"] = json!({"type":"array","items":{"$ref":"#/components/schemas/ApiDashboardCurrencyAmount"},"description":"Original-currency totals for the union of both months. Compare only amounts from the same entry; no exchange-rate conversion."});
}
