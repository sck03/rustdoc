//! Optional PDF page previews extend existing authorization and response contracts.
use serde_json::{Value, json};
pub(super) const REQUESTS: &[&str] = &[
    "ApiReportTemplatePreviewRequest",
    "ApiReportHtmlPreviewRequest",
    "ApiPaymentReportHtmlPreviewRequest",
];
pub(super) const RESPONSES: &[&str] = &[
    "ApiReportTemplatePreviewResponse",
    "ApiReportHtmlPreviewResponse",
    "ApiPaymentReportHtmlPreviewResponse",
];
#[cfg(test)]
pub(super) const REQUEST_FIELDS: &[&str] = &["previewFormat", "pageIndex", "previewRevision"];
#[cfg(test)]
pub(super) const RESPONSE_FIELDS: &[&str] = &["pageIndex", "pageCount", "previewRevision"];
pub(super) fn extend(doc: &mut Value) {
    let schemas = &mut doc["components"]["schemas"];
    for schema in REQUESTS {
        let properties = &mut schemas[schema]["properties"];
        properties["previewFormat"] =
            json!({"type":"string","enum":["Svg","PdfPage"],"default":"Svg"});
        properties["pageIndex"] = json!({"type":"integer","minimum":0,"maximum":499,"default":0});
        properties["previewRevision"] = json!({"type":["string","null"],"maxLength":64,"description":"翻页时提交上一页修订号；单据或模板变化返回 409。"});
    }
    for schema in RESPONSES {
        let properties = &mut schemas[schema]["properties"];
        for field in ["pageIndex", "pageCount"] {
            properties[field] = json!({"type":["integer","null"],"minimum":0});
        }
        properties["previewRevision"] = json!({"type":["string","null"]});
    }
}
