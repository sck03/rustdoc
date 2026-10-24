use serde_json::{Value, json};

pub(super) fn extend(document: &mut Value) {
    document["paths"]["/api/master-data/products"]["get"]["parameters"]
        .as_array_mut().expect("product list parameters").push(json!({
            "name":"productCode", "in":"query", "required":false,
            "description":"Exact normalized product code; authorization applies before returning the match.",
            "schema":{"type":"string","minLength":1,"maxLength":100}
        }));
}
