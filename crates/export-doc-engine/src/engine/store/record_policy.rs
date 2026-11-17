//! Record preparation owns business identity and scope rules; Store owns atomic persistence.
use super::{Actor, Result, department_identity, invoice_identity, normalize, personnel_identity};
use serde_json::{Value, json};

pub(super) struct Revision<'a> {
    pub id: i64,
    pub version: i64,
    pub timestamp: &'a str,
    pub previous: Option<&'a Value>,
    pub inherited_scope: Option<&'a Value>,
}
pub(super) fn prepare(
    kind: &str,
    body: &mut Value,
    identity: Option<String>,
    actor: &Actor,
    action: &str,
    revision: Revision<'_>,
) -> Result<Option<String>> {
    let Revision {
        id,
        version,
        timestamp: now,
        previous,
        inherited_scope: scope,
    } = revision;
    body["ownerUserId"] = json!(actor.id);
    let primary_transfer = kind == "people" && action == "primary-transfer";
    if kind != "users" && !primary_transfer {
        body["companyScope"] = json!(actor.company);
    }
    if !matches!(kind, "people" | "users" | "bookings" | "supply-requests") {
        body["departmentId"] = json!(actor.department);
    } else if kind != "users" && body["departmentId"].as_str().is_none_or(str::is_empty) {
        body["departmentId"] = json!(actor.department);
    }
    if id == 0 {
        body["createdAt"] = json!(now);
        if let Some(scope) = scope {
            for key in ["ownerUserId", "companyScope", "departmentId"] {
                if let Some(value) = scope.get(key) {
                    body[key] = value.clone();
                }
            }
        }
    } else {
        let previous = previous.expect("existing record");
        for key in ["ownerUserId", "companyScope", "departmentId", "createdAt"] {
            if (key == "departmentId" && matches!(kind, "people" | "users"))
                || (key == "companyScope" && (kind == "users" || primary_transfer))
            {
                continue;
            }
            if previous.get(key).is_some() {
                body[key] = previous[key].clone();
            }
        }
    }
    if kind == "departments" {
        body["companyScope"] = body["companyCode"].clone();
    }
    if kind == "companies" {
        body["companyScope"] = body["code"].clone();
    }
    body["updatedAt"] = json!(now);
    body["versionNumber"] = json!(version);
    body["rowVersion"] = json!(format!("native:{version}"));
    if let Some(object) = body.as_object_mut() {
        object.remove("expectedVersion");
        object.remove("resetPassword");
    }
    // The original invoice key is company + invoice number + data type. A
    // length-delimited JSON tuple keeps both data versions and tenant keys distinct.
    let identity = if kind == "departments" {
        Some(department_identity(
            &crate::engine::records::text(body, "companyCode"),
            &crate::engine::records::text(body, "code"),
        )?)
    } else if kind == "people" {
        Some(personnel_identity(
            body["companyScope"].as_str().unwrap_or(""),
            body["employeeNumber"].as_str().unwrap_or(""),
        )?)
    } else if kind == "invoices" {
        Some(invoice_identity(
            &body["companyScope"],
            &body["invoiceNo"],
            &body["type"],
        )?)
    } else {
        identity
            .filter(|key| !key.is_empty())
            .map(|key| normalize(&key))
    };
    if id > 0 {
        identify_record(body, kind, id);
    }
    Ok(identity)
}

pub(super) fn identify_record(body: &mut Value, kind: &str, id: i64) {
    body["id"] = json!(id);
    if kind == "invoices" {
        for item in body["items"].as_array_mut().into_iter().flatten() {
            item["invoiceId"] = json!(id);
        }
    }
    if kind == "people" {
        body["employee"]["id"] = json!(id);
    }
}
