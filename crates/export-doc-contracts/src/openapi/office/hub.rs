use super::{endpoint, object, reference};
use serde_json::{Value, json};
pub(super) fn extend(doc: &mut Value) {
    doc["components"]["schemas"]["OaApprovalHubItem"] = object(
        json!({"id":{"type":"integer","format":"int64"},"employeeId":{"type":"integer","format":"int64"},"employeeNumber":{"type":"string"},"employeeName":{"type":"string"},"title":{"type":"string"}}),
        &[
            "id",
            "employeeId",
            "employeeNumber",
            "employeeName",
            "title",
        ],
    );
    doc["components"]["schemas"]["OaApprovalHubPage"] = object(
        json!({"items":{"type":"array","maxItems":5,"items":reference("OaApprovalHubItem")},"totalCount":{"type":"integer","format":"int64"},"pageNumber":{"type":"integer","format":"int64"},"pageSize":{"type":"integer","format":"int64"}}),
        &["items", "totalCount", "pageNumber", "pageSize"],
    );
    doc["components"]["schemas"]["OaApprovalHubEntry"] = object(
        json!({
            "kind":{"type":"string","enum":["personnel","leave","overtime","expense","travel","purchase","general","rooms","supplies"]},
            "finance":{"type":"boolean"},"handling":{"type":"boolean"},"status":{"type":"string"},"page":reference("OaApprovalHubPage")
        }),
        &["kind", "finance", "handling", "status", "page"],
    );
    doc["components"]["schemas"]["OaApprovalHub"] = object(
        json!({"entries":{"type":"array","maxItems":19,"items":reference("OaApprovalHubEntry")}}),
        &["entries"],
    );
    endpoint(
        doc,
        "/api/office/approval-hub",
        "get",
        "GetOaApprovalHub",
        "office.approval-hub",
        "view",
        None,
        "OaApprovalHub",
        "approval-hub",
        "list",
    );
    let catalog = &mut doc["x-exportdoc-permissions"];
    catalog["resources"].as_array_mut().unwrap().push(json!({"key":"office.approval-hub","name":"审批中心查询","group":"工作台","workspace":"office","moduleKey":"office.approval-hub","sortOrder":390,"isTechnical":true,"supportsDataScope":false,
        "actions":[{"key":"view","name":"读取当前申请队列","description":"按各业务的当前授权读取计数和前五条，不增加数据权限。","sortOrder":0,"navigationAccessLevel":"view"}]}));
    catalog["modules"].as_array_mut().unwrap().push(json!({"key":"office.approval-hub","name":"审批中心查询","group":"工作台","workspace":"office","sortOrder":390,"isTechnical":true}));
    catalog["editions"]["Full"]
        .as_array_mut()
        .unwrap()
        .push(json!("office.approval-hub"));
    for resource in [
        "office.personnel-changes",
        "office.leave",
        "office.overtime",
        "office.expenses",
        "office.travel",
        "office.purchase",
        "office.general",
        "office.rooms",
        "office.supplies",
    ] {
        let dependencies = catalog["dependencies"].as_array_mut().unwrap();
        if let Some(d) = dependencies
            .iter_mut()
            .find(|d| d["resourceKey"] == resource && d["action"] == "view")
        {
            d["grants"].as_array_mut().unwrap().push(
                json!({"resourceKey":"office.approval-hub","action":"view","dataScope":"all"}),
            );
        } else {
            dependencies.push(json!({"resourceKey":resource,"action":"view","grants":[{"resourceKey":"office.approval-hub","action":"view","dataScope":"all"}]}));
        }
    }
    for methods in doc["paths"].as_object_mut().unwrap().values_mut() {
        for endpoint in methods.as_object_mut().unwrap().values_mut() {
            if matches!(
                endpoint["operationId"].as_str(),
                Some("ListMeetingBookings" | "ListOfficeSupplyRequests")
            ) {
                endpoint["parameters"]
                    .as_array_mut()
                    .unwrap()
                    .push(json!({"name":"approvalsOnly","in":"query","schema":{"type":"boolean"}}));
            }
        }
    }
}
