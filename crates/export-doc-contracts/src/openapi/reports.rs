//! Additive invoice image and report preview contracts.
use serde_json::{Value, json};

pub(super) const FIELDS: &[(&str, &[&str])] = &[
    ("ApiInvoiceDetailDto", &["docSealPath", "customsSealPath"]),
    ("ApiReportTemplatePreviewRequest", &["sampleProfile"]),
    ("ApiReportHtmlPreviewRequest", &["content"]),
    ("ApiPaymentReportHtmlPreviewRequest", &["content"]),
];

pub(super) const MANAGED_FILES: &[&str] = &[
    "CheckReportTemplateStorage",
    "CreateReportTemplate",
    "DeleteReportTemplate",
    "RenameReportTemplate",
    "UpdateReportTemplateDisplayName",
    "SaveReportTemplateContent",
    "SetDefaultReportTemplate",
    "ImportReportTemplateFile",
    "ImportReportTemplatePackage",
    "UploadReportTemplateFile",
    "UploadReportTemplatePackage",
];

pub(super) fn extend(document: &mut Value) {
    for methods in document["paths"].as_object_mut().unwrap().values_mut() {
        for operation in methods.as_object_mut().unwrap().values_mut() {
            if MANAGED_FILES.contains(&operation["operationId"].as_str().unwrap_or("")) {
                operation["x-exportdoc-policy"]["requirements"]
                    .as_array_mut()
                    .unwrap()
                    .push(json!({"resourceKey":"system.settings","action":"manage"}));
            }
        }
    }
    for (schema, fields) in FIELDS {
        for field in *fields {
            document["components"]["schemas"][schema]["properties"][field] = if *schema
                == "ApiInvoiceDetailDto"
            {
                json!({"type":["null","string"],"description":"受控发票印章；null 继承出口商档案，空字符串不盖章。"})
            } else {
                json!({"type":"string"})
            };
        }
    }
    let mut endpoint = document["paths"]["/api/invoices/shipping-marks/image"].clone();
    endpoint["post"]["operationId"] = json!("SaveInvoiceSealImage");
    endpoint["post"]["requestBody"]["content"]["application/json"]["schema"]["$ref"] =
        json!("#/components/schemas/ApiInvoiceSealImageSaveRequest");
    endpoint["post"]["responses"]["200"]["content"]["application/json"]["schema"]["$ref"] =
        json!("#/components/schemas/ApiInvoiceSealImageSaveResponse");
    document["components"]["schemas"]["ApiInvoiceSealImageSaveRequest"] =
        document["components"]["schemas"]["ApiShippingMarkImageSaveRequest"].clone();
    document["components"]["schemas"]["ApiInvoiceSealImageSaveResponse"] =
        document["components"]["schemas"]["ApiShippingMarkImageSaveResponse"].clone();
    document["paths"]["/api/invoices/seals/image"] = endpoint;
}
