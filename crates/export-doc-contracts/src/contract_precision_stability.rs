//! Independent review of decimal text, probe semantics and retired update routes.
use serde_json::{Value, json};

pub(super) fn apply(baseline: &mut Value) {
    baseline["x-exportdoc-shared-schemas"] = json!([
        "ApiReportTemplateV3LimitsContract",
        "ApiReportTemplateV3PageContract"
    ]);
    for schema in [
        "ApiCustomsCooProducerProfileResponse",
        "ApiExchangeRateAvailableCurrenciesResponse",
        "ApiReportTemplateV3ContractResponse",
        "ApiReportTemplateV3ExpressionContract",
        "ApiReportTemplateV3ImageResourceContract",
        "ApiReportTemplateV3ReleaseContract",
        "ApiReportTemplateV3RepeatBandContract",
    ] {
        assert!(
            baseline["components"]["schemas"]
                .as_object_mut()
                .unwrap()
                .remove(schema)
                .is_some()
        );
    }
    for name in [
        "ApiInvoiceDetailDto",
        "ApiInvoiceItemDto",
        "ApiInvoiceListItemDto",
        "ApiInvoiceProfitAnalysisResponse",
        "ApiQueryInvoiceRowDto",
        "ApiDashboardRecentInvoiceDto",
        "ApiProductDto",
        "ApiDashboardResponse",
        "ApiDashboardCurrencyAmount",
    ] {
        for field in baseline["components"]["schemas"][name]["properties"]
            .as_object_mut()
            .unwrap()
            .values_mut()
        {
            if field["type"]
                .as_array()
                .is_some_and(|types| types.contains(&json!("number")))
            {
                let nullable = field["type"]
                    .as_array()
                    .unwrap()
                    .contains(&Value::String("null".into()));
                field["type"] = if nullable {
                    json!(["null", "string"])
                } else {
                    json!("string")
                };
                field["format"] = json!("decimal");
                field["pattern"] = json!("^[+-]?[0-9]+(?:\\.[0-9]+)?$");
                field["maxLength"] = json!(60);
                field["default"] = if nullable { Value::Null } else { json!("0") };
                field.as_object_mut().unwrap().remove("minimum");
                field.as_object_mut().unwrap().remove("maximum");
            }
        }
    }
    let paths = baseline["paths"].as_object_mut().unwrap();
    for path in [
        "/api/single-window/coo/producer-profiles/{id}",
        "/api/tools/exchange-rates/available-currencies",
        "/api/reports/templates/v3/contract",
    ] {
        assert!(paths[path].as_object_mut().unwrap().remove("get").is_some());
        if paths[path].as_object().unwrap().is_empty() {
            paths.remove(path);
        }
    }
    for path in [
        "/api/office/bookings/{id}",
        "/api/office/supply-requests/{id}",
    ] {
        assert!(paths[path].as_object_mut().unwrap().remove("put").is_some());
        if paths[path].as_object().unwrap().is_empty() {
            paths.remove(path);
        }
    }
    for (path, kind) in [("/livez", "liveness"), ("/readyz", "readiness")] {
        for method in ["get", "head"] {
            let operation = &mut paths[path][method];
            operation["x-exportdoc-probe"] = json!(kind);
            operation["x-exportdoc-timeout-seconds"] = json!(1);
            operation["x-exportdoc-json-response-limit-bytes"] = json!(65536);
            if kind == "readiness" {
                operation["responses"]["503"] = json!({"description":"Service Unavailable","content":{"application/json":{"schema":{"$ref":"#/components/schemas/ApiErrorResponse"}}}});
            }
        }
    }
    paths["/api/master-data/hs-codes/fetch-remote-detail"]["post"]["x-exportdoc-policy"]["access"]
        ["resources"][0]["actions"] = json!(["view"]);
    for path in paths.values_mut() {
        for operation in path.as_object_mut().unwrap().values_mut() {
            if operation["responses"]
                .as_object()
                .unwrap()
                .iter()
                .any(|(code, response)| {
                    code.starts_with('2')
                        && [
                            "ApiDownloadTicket",
                            "ApiLogoutResponse",
                            "ApiCrmCustomerBatchStatusResult",
                        ]
                        .iter()
                        .any(|name| {
                            response["content"]["application/json"]["schema"]["$ref"]
                                == format!("#/components/schemas/{name}")
                        })
                })
            {
                operation["x-exportdoc-json-response-limit-bytes"] = json!(65536);
            }
        }
    }
}
