//! Independent, immutable source snapshots. COO and ACD own their editable drafts.
use super::*;
const KIND: &str = "sw-sources";

pub fn load(tx: &Connection, actor: &Actor, id: i64, action: &str) -> Result<Value> {
    let (value, invoice) = match tx.get("invoices", id)? {
        Some(value) => (value, true),
        None => (store::get(tx, KIND, id)?, false),
    };
    if !auth::visible(actor, PERMISSION, action, &value)
        || invoice && !auth::visible(actor, "document.invoices", "view", &value)
    {
        return Err(error(403, "没有此申报资料的操作权限。"));
    }
    Ok(value)
}

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    if operation == LIST_SINGLE_WINDOW_SOURCES {
        return service
            .store
            .read_for(actor, operation, query, |tx, actor| {
                let page = parameter(query, "pageNumber")
                    .parse::<i64>()
                    .unwrap_or(1)
                    .max(1);
                let size = parameter(query, "pageSize")
                    .parse::<i64>()
                    .unwrap_or(50)
                    .clamp(1, 200);
                let (total, rows) = tx.query_ordered_record_fields(
                    &export_doc_storage::GenericQuery {
                        kind: KIND,
                        scope: auth::data_scope(actor, PERMISSION, "view"),
                        keyword: &store::normalize(parameter(query, "keyword")),
                        offset: page.saturating_sub(1).saturating_mul(size),
                        limit: size,
                        ..Default::default()
                    },
                    &["invoiceNo", "sourceKind"],
                    export_doc_storage::RecordOrder::UpdatedDescending,
                )?;
                Ok(contracts::dto(
                    contracts::response(operation.id),
                    json!({"items":rows,"totalCount":total}),
                ))
            });
    }
    export_doc_contracts::validation::structure(contracts::request(operation.id), body)
        .map_err(invalid)?;
    let reference = rules::text(body, "referenceNo");
    if reference.is_empty() || reference.chars().any(char::is_control) {
        return Err(invalid("请填写有效的申报资料编号。"));
    }
    let imported = body.get("invoice").is_some();
    let schema = contracts::schema("ApiInvoiceDetailDto");
    let mut snapshot = if imported {
        contracts::dto(schema, body["invoice"].clone())
    } else {
        contracts::initial(schema)
    };
    snapshot["invoiceNo"] = json!(reference);
    snapshot["sourceKind"] = json!(if imported { "Excel" } else { "Manual" });
    // Excel preview IDs are not business identities. Assign stable identities within this snapshot.
    if let Some(items) = snapshot["items"].as_array_mut() {
        if items.len() > 5000 {
            return Err(invalid("申报资料最多 5000 行。"));
        }
        for (index, item) in items.iter_mut().enumerate() {
            item["id"] = json!(index + 1);
        }
    }
    // Do not accept client-supplied links to protected master data or ownership.
    for key in ["id", "exporterId", "customerId"] {
        snapshot[key] = json!(0);
    }
    service
        .store
        .transaction_for(actor, operation, &[], |tx, actor| {
            let identity =
                serde_json::to_string(&[actor.company.as_str(), &store::normalize(reference)])?;
            let saved = store::save(tx, KIND, 0, snapshot, Some(identity), actor, "create")?;
            Ok(contracts::dto(contracts::response(operation.id), saved))
        })
}
