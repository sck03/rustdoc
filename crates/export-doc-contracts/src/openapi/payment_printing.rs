//! Shared voucher use is independent of export documents and template design.
use serde_json::{Value, json};

pub(super) const CATALOG: &str = "common.report-catalog";

pub(super) fn extend(doc: &mut Value) {
    let permissions = &mut doc["x-exportdoc-permissions"];
    permissions["modules"].as_array_mut().unwrap().push(json!({
        "key":CATALOG,"name":"可用打印模板","group":"通用基础能力",
        "workspace":"common","sortOrder":86,"isTechnical":true
    }));
    permissions["resources"].as_array_mut().unwrap().push(json!({
        "key":CATALOG,"name":"可用打印模板","group":"通用基础能力","workspace":"common",
        "moduleKey":CATALOG,"sortOrder":361,"isTechnical":true,"supportsDataScope":false,
        "actions":[{"key":"view","name":"选择","description":"按单据数据域选择本人或已共享模板，不授予模板管理权限",
            "sortOrder":10,"navigationAccessLevel":"view"}]
    }));
    for section in ["modules", "resources"] {
        for item in permissions[section].as_array_mut().unwrap() {
            if matches!(
                item["key"].as_str(),
                Some(
                    "document.payments"
                        | "document.jobs"
                        | "document.reports"
                        | "document.payment-reports"
                        | "document.payment-output"
                        | "document.report-templates"
                        | "document.report-resources"
                )
            ) {
                item["workspace"] = json!("common");
                item["group"] = json!("付款报销与打印");
            }
        }
    }
    for edition in ["Document", "Full"] {
        permissions["editions"][edition]
            .as_array_mut()
            .unwrap()
            .push(json!(CATALOG));
    }
    let dependencies = permissions["dependencies"].as_array_mut().unwrap();
    for dependency in dependencies.iter_mut() {
        let output = matches!(
            dependency["resourceKey"].as_str(),
            Some("document.payment-output" | "document.invoice-output")
        );
        let read_payment =
            dependency["resourceKey"] == "document.payments" && dependency["action"] == "view";
        for grant in dependency["grants"].as_array_mut().unwrap() {
            if output && grant["resourceKey"] == "document.report-templates" {
                grant["resourceKey"] = json!(CATALOG);
            }
            if (output || read_payment) && grant["resourceKey"] == "document.custom-options" {
                grant["action"] = json!("view");
            }
        }
        if output
            && matches!(
                dependency["action"].as_str(),
                Some("export-pdf" | "export-zip")
            )
        {
            dependency["grants"].as_array_mut().unwrap().push(json!({
                "resourceKey":"document.jobs","action":"view","dataScope":"own"
            }));
        }
    }
    dependencies.push(
        json!({"resourceKey":"document.report-templates","action":"view",
        "grants":[{"resourceKey":CATALOG,"action":"view"}]}),
    );
    for role in permissions["roles"].as_array_mut().unwrap() {
        let finance = role["code"] == "Finance";
        let grants = role["grants"].as_array_mut().unwrap();
        if finance {
            grants.retain(|g| g["resourceKey"] != "document.query");
            for grant in grants.iter_mut() {
                if (grant["resourceKey"] == "document.payments" && grant["action"] == "view")
                    || grant["resourceKey"] == "document.payment-output"
                {
                    grant["dataScope"] = json!("company");
                }
            }
        }
        for (resource, actions) in [
            ("document.payments", &["view", "operate"][..]),
            (
                "document.payment-output",
                &["preview", "print", "export-pdf"][..],
            ),
            (
                "document.report-templates",
                &["view", "design", "clone"][..],
            ),
            ("document.report-resources", &["upload"][..]),
        ] {
            for action in actions {
                if !grants
                    .iter()
                    .any(|g| g["resourceKey"] == resource && g["action"] == *action)
                {
                    let scope = if resource == "document.report-templates" && *action == "view" {
                        "department"
                    } else {
                        "own"
                    };
                    grants.push(json!({"resourceKey":resource,"action":action,"dataScope":scope}));
                }
            }
        }
    }
    let policy = &mut doc["paths"]["/api/reports/templates"]["get"]["x-exportdoc-policy"];
    policy["requirements"] = json!([{"resourceKey":CATALOG,"action":"view"}]);
    policy["permissions"]["readModule"] = json!(CATALOG);
}
