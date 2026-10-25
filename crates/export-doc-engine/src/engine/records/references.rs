//! Deletion integrity uses complete storage facts, independently of display history limits.
use super::{Connection, Result, Value, conflict, text};

pub fn check_references(connection: &Connection, kind: &str, record: &Value) -> Result<()> {
    super::super::oa::approval_references::check(connection, kind, record)?;
    super::super::communication::check_references(connection, kind, record)?;
    if matches!(kind, "users" | "people")
        && super::super::oa::references(
            connection,
            &text(record, "companyScope"),
            if kind == "people" {
                record["id"].as_i64()
            } else {
                None
            },
            if kind == "users" {
                record["id"].as_i64()
            } else {
                None
            },
            false,
        )? > 0
    {
        return Err(conflict("账号或人员已有行政审批历史，不能删除。"));
    }
    if matches!(kind, "companies" | "departments") {
        let company = if kind == "companies" {
            text(record, "code")
        } else {
            text(record, "companyCode")
        };
        let department = text(record, "code");
        for request_kind in super::super::oa::KINDS {
            if connection
                .query_records(&export_doc_storage::RecordQuery {
                    kind: request_kind,
                    company: &company,
                    department: (kind == "departments").then_some(department.as_str()),
                    limit: 1,
                    ..Default::default()
                })?
                .0
                > 0
            {
                return Err(conflict("组织已有行政审批记录，不能删除。"));
            }
        }
    }
    const REFS: &[(&str, &str, &str)] = &[
        ("users", "people", "account.id"),
        ("users", "bookings", "ownerUserId"),
        ("users", "supply-requests", "ownerUserId"),
        ("customers", "invoices", "customerId"),
        ("exporters", "invoices", "exporterId"),
        ("payees", "payments", "payeeId"),
        ("crm-customers", "crm-contacts", "crmCustomerId"),
        ("crm-customers", "crm-follow-ups", "crmCustomerId"),
        ("crm-customers", "opportunities", "crmCustomerId"),
        ("suppliers", "supplier-contacts", "supplierCompanyId"),
        ("suppliers", "supplier-products", "supplierCompanyId"),
        ("suppliers", "supplier-assessments", "supplierCompanyId"),
        ("products", "supplier-products", "productId"),
        ("products", "opportunities", "productId"),
        ("rooms", "bookings", "meetingRoomId"),
        ("supplies", "supply-requests", "officeSupplyId"),
        ("supplies", "stock-movements", "officeSupplyId"),
        ("people", "bookings", "employeeId"),
        ("people", "supply-requests", "employeeId"),
        ("permission-templates", "users", "permissionTemplateId"),
        ("invoices", "attachments", "invoiceId"),
        ("attachment-categories", "attachments", "categoryId"),
    ];
    for (parent, child, property) in REFS.iter().filter(|(parent, _, _)| *parent == kind) {
        let _ = parent;
        if connection
            .query_generic(&export_doc_storage::GenericQuery {
                kind: child,
                scope: export_doc_storage::DataScope {
                    all: true,
                    ..Default::default()
                },
                filters: &[(*property, record["id"].to_string())],
                keyword: "",
                search_root: None,
                fields: &[("id", "id")],
                offset: 0,
                limit: 1,
            })?
            .0
            > 0
        {
            return Err(conflict("该记录已被业务引用，不能删除。"));
        }
    }
    if ["companies", "departments"].contains(&kind) {
        super::super::organization::check_references(connection, kind, record)?;
    }
    Ok(())
}
