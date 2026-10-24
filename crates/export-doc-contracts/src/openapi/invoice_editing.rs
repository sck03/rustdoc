use serde_json::{Value, json};
pub(super) fn extend(document: &mut Value) {
    let maximum = crate::invoice_editing::MAX_ITEMS;
    document["components"]["schemas"]["ApiInvoiceEditingLimitsContract"] = json!({
        "type":"object", "additionalProperties":false, "required":["maxItems"],
        "properties":{"maxItems":{"type":"integer","const":maximum,"default":maximum}}
    });
}
