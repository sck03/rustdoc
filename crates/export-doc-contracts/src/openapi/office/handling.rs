use super::super::{endpoint, object, reference};
use serde_json::{Value, json};

pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let text = json!({"type":"string"});
    s["PersonnelClearance"]["properties"]["handlingCount"] =
        json!({"type":"integer","format":"int64"});
    s["PersonnelClearance"]["properties"]["handlingServices"] =
        json!({"type":"array","items":{"type":"string"}});
    s["OfficeHandlingService"] = object(
        json!({
            "key":text,"name":text,
            "category":{"type":"string","enum":["Seal","Certificate","IT","Repair","Other","Supply"]},
            "handlerUserIds":{"type":"array","items":{"type":"integer","format":"int64"},"minItems":1,"maxItems":10},
            "handlerNames":text,"isActive":{"type":"boolean"}
        }),
        &["key", "name", "category", "handlerUserIds", "isActive"],
    );
    let services =
        json!({"type":"array","items":reference("OfficeHandlingService"),"maxItems":100});
    for name in ["OaApprovalSettings", "OaApprovalSettingsSave"] {
        s[name]["properties"]["handlingServices"] = services.clone();
    }
    s["OfficeHandlingDirectory"] = object(json!({"items":services}), &["items"]);
    for name in [
        "OaRequestSave",
        "OaRequest",
        "OfficeSupplySaveRequest",
        "OfficeSupplyRecord",
        "OfficeSupplyRequestRecord",
    ] {
        s[name]["properties"]["handlingKey"] = text.clone();
    }
    for name in [
        "OaRequest",
        "OfficeSupplyRecord",
        "OfficeSupplyRequestRecord",
    ] {
        s[name]["properties"]["handlingName"] = text.clone();
        s[name]["properties"]["handlerNames"] = text.clone();
        s[name]["properties"]["canHandle"] = json!({"type":"boolean"});
    }
    for (id, path, resource, action) in [
        (
            "ListGeneralHandlingServices",
            "/api/office/general-handling-services",
            "office.general",
            "handling-general",
        ),
        (
            "ListSupplyHandlingServices",
            "/api/office/supply-handling-services",
            "office.supplies",
            "handling-supply",
        ),
    ] {
        endpoint(
            doc,
            path,
            "get",
            id,
            resource,
            "view",
            None,
            "OfficeHandlingDirectory",
            "approval-settings",
            action,
        );
    }
    for kind in ["leave", "overtime", "travel", "purchase", "general"] {
        doc["paths"][format!("/api/office/{kind}-requests")]["get"]["parameters"]
            .as_array_mut()
            .unwrap()
            .push(json!({"name":"handlingOnly","in":"query","schema":{"type":"boolean"}}));
    }
    doc["paths"]["/api/office/supply-requests"]["get"]["parameters"]
        .as_array_mut()
        .unwrap()
        .push(json!({"name":"handlingOnly","in":"query","schema":{"type":"boolean"}}));
}
