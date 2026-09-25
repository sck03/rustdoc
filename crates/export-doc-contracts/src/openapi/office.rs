mod schemas;
use serde_json::{Value, json};

fn reference(name: &str) -> Value {
    json!({"$ref":format!("#/components/schemas/{name}")})
}
fn object(properties: Value, required: &[&str]) -> Value {
    json!({"type":"object","properties":properties,"required":required})
}

pub(super) fn extend(doc: &mut Value) {
    schemas::extend(doc);
    for section in ["resources", "modules"] {
        for item in doc["x-exportdoc-permissions"][section]
            .as_array_mut()
            .unwrap()
        {
            match item["key"].as_str() {
                Some("office.people") => item["group"] = json!("人事管理"),
                Some("office.rooms" | "office.supplies") => item["group"] = json!("行政办公"),
                _ => {}
            }
        }
    }
    for (kind, name, resource, label, group, order) in [
        (
            "leave",
            "Leave",
            "office.leave",
            "员工请假",
            "人事管理",
            350,
        ),
        (
            "overtime",
            "Overtime",
            "office.overtime",
            "加班申请",
            "人事管理",
            360,
        ),
        (
            "expense",
            "Expense",
            "office.expenses",
            "费用报销",
            "行政办公",
            370,
        ),
        (
            "travel",
            "Travel",
            "office.travel",
            "出差申请",
            "行政办公",
            380,
        ),
        (
            "purchase",
            "Purchase",
            "office.purchase",
            "采购申请",
            "行政办公",
            390,
        ),
        (
            "general",
            "General",
            "office.general",
            "通用申请",
            "行政办公",
            400,
        ),
    ] {
        let path = format!("/api/office/{kind}-requests");
        let mut add = |suffix: &str,
                       method: &str,
                       verb: &str,
                       action: &str,
                       permission: &str,
                       body: Option<&str>,
                       response: &str| {
            endpoint(
                doc,
                &format!("{path}{suffix}"),
                method,
                &format!("{verb}{name}Request"),
                resource,
                permission,
                body,
                response,
                kind,
                action,
            );
        };
        add("", "get", "List", "list", "view", None, "OaRequestPage");
        add(
            "",
            "post",
            "Create",
            "create",
            "create",
            Some("OaRequestSave"),
            "OaRequest",
        );
        add("/{id}", "get", "Get", "get", "view", None, "OaRequest");
        add(
            "/{id}",
            "put",
            "Update",
            "update",
            "edit",
            Some("OaRequestSave"),
            "OaRequest",
        );
        add(
            "/{id}/history",
            "get",
            "ListHistoryOf",
            "history",
            "view",
            None,
            "OaEventPage",
        );
        for (action, verb, permission) in [
            ("submit", "Submit", "edit"),
            ("withdraw", "Withdraw", "edit"),
            ("approve", "Approve", "approve"),
            ("reject", "Reject", "approve"),
            ("cancel", "Cancel", "cancel"),
            ("void", "Void", "approve"),
            ("complete", "Complete", "complete"),
        ] {
            add(
                &format!("/{{id}}/{action}"),
                "post",
                verb,
                action,
                permission,
                Some("OaAction"),
                "OaRequest",
            );
        }
        add(
            "/{id}/attachments",
            "post",
            "UploadAttachmentTo",
            "upload",
            "edit",
            None,
            "OaRequest",
        );
        add(
            "/{id}/attachments/{attachmentId}",
            "get",
            "DownloadAttachmentOf",
            "download",
            "view",
            None,
            "OaAttachment",
        );
        add(
            "/{id}/attachments/{attachmentId}",
            "delete",
            "DeleteAttachmentOf",
            "delete-attachment",
            "edit",
            Some("OaAction"),
            "OaRequest",
        );
        let upload = &mut doc["paths"][format!("{path}/{{id}}/attachments")]["post"];
        upload["requestBody"] = json!({"required":true,"content":{"multipart/form-data":{"schema":object(json!({"file":{"type":"string","format":"binary"},"expectedVersion":{"type":"integer","format":"int64"}}), &["file","expectedVersion"])}}});
        doc["paths"][format!("{path}/{{id}}/attachments/{{attachmentId}}")]["get"]["responses"]["200"]
            ["content"] =
            json!({"application/octet-stream":{"schema":{"type":"string","format":"binary"}}});
        permissions(doc, kind, resource, label, group, order);
    }
}

fn permissions(doc: &mut Value, kind: &str, resource: &str, label: &str, group: &str, order: i32) {
    let catalog = &mut doc["x-exportdoc-permissions"];
    let actions: Vec<_> = [("view","查看","view"),("create","新建申请","operate"),("edit","编辑与提交","operate"),("cancel","取消草稿","operate"),("approve","审批与作废","manage"),("complete",if kind=="expense" {"移交财务"} else {"完成登记"},"manage")].into_iter().enumerate().map(|(order,(key,name,level))| json!({"key":key,"name":name,"description":name,"sortOrder":order*10,"navigationAccessLevel":level})).collect();
    catalog["resources"].as_array_mut().unwrap().push(json!({"key":resource,"name":label,"group":group,"workspace":"office","moduleKey":resource,"sortOrder":order,"isTechnical":false,"supportsDataScope":true,"actions":actions}));
    catalog["modules"].as_array_mut().unwrap().push(json!({"key":resource,"name":label,"group":group,"workspace":"office","sortOrder":order,"isTechnical":false}));
    for edition in ["Full", "Administration"] {
        catalog["editions"][edition]
            .as_array_mut()
            .unwrap()
            .push(json!(resource));
    }
    for role in catalog["roles"].as_array_mut().unwrap() {
        let code = role["code"].as_str().unwrap().to_owned();
        let manager = code == "Admin"
            || if matches!(kind, "leave" | "overtime") {
                code == "PersonnelManager"
            } else {
                code == "OfficeManager"
            };
        for action in &actions {
            let key = action["key"].as_str().unwrap();
            if matches!(key, "approve" | "complete") && !manager {
                continue;
            }
            let scope = if manager { "company" } else { "own" };
            role["grants"]
                .as_array_mut()
                .unwrap()
                .push(json!({"resourceKey":resource,"action":key,"dataScope":scope}));
        }
    }
}

#[allow(clippy::too_many_arguments)]
fn endpoint(
    doc: &mut Value,
    path: &str,
    method: &str,
    id: &str,
    resource: &str,
    permission: &str,
    body: Option<&str>,
    response: &str,
    kind: &str,
    action: &str,
) {
    let mut op = doc["paths"]["/api/office/supply-requests"]["post"].clone();
    op["operationId"] = json!(id);
    op["tags"] = json!(["Office approvals"]);
    op["x-exportdoc-office"] =
        json!({"kind":kind,"action":action,"resource":resource,"permission":permission});
    op["x-exportdoc-policy"]["requirements"] =
        json!([{"resourceKey":resource,"action":permission}]);
    op["responses"]["200"]["content"]["application/json"]["schema"] = reference(response);
    for (code, message) in [("401", "Unauthorized"), ("429", "Too Many Requests")] {
        op["responses"][code] = json!({"description":message,"content":{"application/json":{"schema":reference("ApiErrorResponse")}}});
    }
    op.as_object_mut().unwrap().remove("requestBody");
    if let Some(body) = body {
        op["requestBody"] =
            json!({"required":true,"content":{"application/json":{"schema":reference(body)}}});
    }
    let mut params = vec![];
    for name in ["id", "attachmentId"] {
        if path.contains(&format!("{{{name}}}")) {
            params.push(json!({"name":name,"in":"path","required":true,"schema":{"type":"integer","format":"int64","minimum":1}}));
        }
    }
    if matches!(action, "list" | "history") {
        for name in ["pageNumber", "pageSize"] {
            params.push(json!({"name":name,"in":"query","schema":{"type":"integer","minimum":1}}));
        }
    }
    if action == "list" {
        params.push(json!({"name":"status","in":"query","schema":{"type":"string"}}));
        params.push(json!({"name":"mineOnly","in":"query","schema":{"type":"boolean"}}));
    }
    if !params.is_empty() {
        op["parameters"] = json!(params);
    }
    if doc["paths"].get(path).is_none() {
        doc["paths"][path] = json!({});
    }
    doc["paths"][path][method] = op;
}
