//! Additive invoice image and report preview contracts.
use serde_json::{Value, json};

pub(super) const FIELDS: &[(&str, &[&str])] = &[
    ("ApiInvoiceDetailDto", &["docSealPath", "customsSealPath"]),
    ("ApiReportTemplatePreviewRequest", &["sampleProfile"]),
    ("ApiReportHtmlPreviewRequest", &["content"]),
    ("ApiPaymentReportHtmlPreviewRequest", &["content"]),
    ("ApiInvoiceReportZipRequest", &["templatePaths"]),
    ("ApiReportTemplateV3ContractResponse", &["batchLimits"]),
    (
        "ApiReportTemplateV3PageContract",
        &[
            "presets",
            "minDimensionHundredthMm",
            "maxDimensionHundredthMm",
        ],
    ),
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
        if *schema == "ApiReportTemplateV3PageContract" {
            continue;
        }
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
    let page =
        &mut document["components"]["schemas"]["ApiReportTemplateV3PageContract"]["properties"];
    page["presets"] = json!({
        "type":"array", "description":"标准纸张竖版尺寸；横版交换宽高。Custom 使用文档自身尺寸。",
        "items":{"type":"object","required":["size","widthHundredthMm","heightHundredthMm"],"properties":{
            "size":{"type":"string"},"widthHundredthMm":{"type":"integer"},"heightHundredthMm":{"type":"integer"}
        }},
        "default":crate::report_pages::PRESETS
    });
    page["minDimensionHundredthMm"] =
        json!({"type":"integer","default":crate::report_pages::MIN_DIMENSION});
    page["maxDimensionHundredthMm"] =
        json!({"type":"integer","default":crate::report_pages::MAX_DIMENSION});
    let schemas = &mut document["components"]["schemas"];
    schemas["ApiInvoiceReportZipRequest"]["properties"]["templatePaths"] = json!({
        "type":["array","null"], "default":null, "minItems":1, "maxItems":crate::report_batches::MAX_TEMPLATES, "uniqueItems":true,
        "items":{"type":"string","minLength":1},
        "description":"为每张发票生成所有选中模板。未提供或为 null 时使用原 templatePath；总 PDF 数受 batchLimits 限制。"
    });
    schemas["ApiReportTemplateV3ContractResponse"]["properties"]["batchLimits"] =
        json!({"$ref":"#/components/schemas/ApiInvoiceReportBatchLimitsContract"});
    schemas["ApiInvoiceReportBatchLimitsContract"] = json!({"type":"object","properties":{
        "maxInvoices":{"type":"integer","default":crate::report_batches::MAX_INVOICES},
        "maxTemplates":{"type":"integer","default":crate::report_batches::MAX_TEMPLATES},
        "maxReports":{"type":"integer","default":crate::report_batches::MAX_REPORTS},
        "maxArchiveBytes":{"type":"integer","default":crate::report_batches::MAX_ARCHIVE_BYTES}
    }});
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
