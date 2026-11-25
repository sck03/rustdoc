//! Independently reviewed additive payment goods contract; endpoint metadata remains frozen.
use serde_json::{Value, json};

pub(super) fn apply(baseline: &mut Value) {
    baseline["components"]["schemas"]["ApiPaymentGoodsItem"] = json!({
        "type":"object", "additionalProperties":false,
        "properties":{
            "goodsName":{"type":"string","maxLength":500},
            "quantity":{"type":"string","maxLength":100},
            "quantityUnit":{"type":"string","maxLength":20}
        }, "required":["goodsName","quantity","quantityUnit"]
    });
    baseline["components"]["schemas"]["ApiPaymentDto"]["properties"]["goodsItems"] = json!({
        "type":"array", "items":{"$ref":"#/components/schemas/ApiPaymentGoodsItem"},
        "default":[], "maxItems":4,
        "description":"本次付款独立货物明细，最多四项；已有单行品名、数量和单位在保存时归入明细。"
    });
}
