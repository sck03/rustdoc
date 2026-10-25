//! Common list queries: permissions and filters precede SQL pagination;
//! the generated response schema determines the transferred fields.
use super::*;
use export_doc_storage::GenericQuery;

pub(super) fn rows(
    store: &Store,
    actor: &Actor,
    resource: &Resource,
    operation: Operation,
    query: &[(&str, String)],
    parameters: &[(&str, String)],
    paginated: bool,
) -> Result<(Vec<Value>, usize, usize, usize)> {
    let parameter = |name| {
        query
            .iter()
            .find(|(key, _)| *key == name)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let mut filters = Vec::new();
    for (key, value) in parameters.iter().chain(query.iter()) {
        if !value.is_empty()
            && [
                "crmCustomerId",
                "customerId",
                "supplierId",
                "employeeId",
                "meetingRoomId",
                "officeSupplyId",
                "departmentId",
            ]
            .contains(key)
        {
            filters.push((
                if *key == "customerId" {
                    "crmCustomerId"
                } else {
                    *key
                },
                value.clone(),
            ));
        }
    }
    if paginated && !parameter("status").is_empty() {
        filters.push((
            if resource.key == "people" {
                "employee.status"
            } else {
                "status"
            },
            parameter("status").into(),
        ));
    }
    let response = contracts::resolve(contracts::response(operation.id));
    let array = if contracts::kind(response) == "array" {
        response
    } else {
        contracts::resolve(
            &response["properties"][match resource.key {
                "users" => "users",
                "permission-templates" => "templates",
                "container-projects" => "projects",
                _ => "items",
            }],
        )
    };
    let item = contracts::resolve(&array["items"]);
    let mut fields: Vec<(&str, &str)> = item["properties"]
        .as_object()
        .into_iter()
        .flat_map(|p| p.keys())
        .map(|name| {
            let source = match (resource.key, name.as_str()) {
                ("invoices", "customerName") => "customerNameEN",
                ("invoices", "exporterName") => "exporterNameEN",
                _ => name.as_str(),
            };
            (name.as_str(), source)
        })
        .collect();
    if resource.key == "people" {
        fields = vec![("employee", "employee")];
    }
    if !fields.is_empty() {
        for name in [
            "id",
            "versionNumber",
            "ownerUserId",
            "companyScope",
            "departmentId",
        ] {
            if !fields.iter().any(|(key, _)| *key == name) {
                fields.push((name, name));
            }
        }
    }
    let (page, size, offset) = store::page_parameters(query);
    let keyword = if paginated {
        store::normalize(parameter("keyword"))
    } else {
        String::new()
    };
    let load = |tx: &Connection, actor: &Actor| -> Result<(Vec<Value>, usize)> {
        if resource.key == "products" {
            if let Some((_, code)) = query.iter().find(|(name, _)| *name == "productCode") {
                let code = store::normalize(code);
                if code.is_empty() || code.encode_utf16().count() > 100 {
                    return Err(invalid("商品编码须为 1–100 个字符。"));
                }
                let row = tx
                    .find_identity("products", &code)?
                    .filter(|row| auth::visible(actor, read_permission(resource), "view", row));
                let total = usize::from(row.is_some());
                return Ok((
                    if offset == 0 {
                        row.into_iter().collect()
                    } else {
                        vec![]
                    },
                    total,
                ));
            }
        }
        let mut items = Vec::new();
        loop {
            crate::operation::check()?;
            let (total, rows) = tx.query_generic(&GenericQuery {
                kind: resource.key,
                scope: auth::data_scope(actor, read_permission(resource), "view"),
                filters: &filters,
                keyword: &keyword,
                search_root: (resource.key == "people").then_some("employee"),
                fields: &fields,
                offset: if paginated {
                    offset
                } else {
                    items.len() as i64
                },
                limit: if paginated { size as i64 } else { 200 },
            })?;
            let empty = rows.is_empty();
            items.extend(rows);
            if paginated || empty || items.len() as i64 >= total {
                return Ok((items, total as usize));
            }
        }
    };
    let (rows, total) = store.read_for(actor, operation, query, load)?;
    Ok((rows, total, page, size))
}
