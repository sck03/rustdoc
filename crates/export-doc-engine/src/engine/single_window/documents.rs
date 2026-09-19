use super::*;
use rules::{draft, mapping, validation};
use std::collections::BTreeSet;
pub struct Loaded {
    pub current: Value,
    pub defaults: Value,
    pub stored: Option<Value>,
    pub locks: BTreeSet<String>,
}
fn stamp(current: &mut Value) {
    let business = if current.get("certType").is_some() {
        Business::Coo
    } else {
        Business::Acd
    };
    let issues = validation::review(business, current);
    current["warningCount"] = json!(issues.len());
    current["warningSummary"] = json!(
        issues
            .iter()
            .take(20)
            .map(|i| i.message.as_str())
            .collect::<Vec<_>>()
            .join("\n")
    );
}
pub fn load(
    tx: &Connection,
    actor: &Actor,
    service: &NativeService,
    business: Business,
    id: i64,
    action: &str,
) -> Result<Loaded> {
    let invoice = source(tx, actor, id, action)?;
    let related = |kind: &str, key: &str| -> Result<Value> {
        let id = invoice[key].as_i64().unwrap_or(0);
        if id > 0 {
            Ok(tx.get(kind, id)?.unwrap_or(Value::Null))
        } else {
            Ok(Value::Null)
        }
    };
    let exporter = related("exporters", "exporterId")?;
    let customer = related("customers", "customerId")?;
    let catalog = references::catalog(tx)?;
    let preferences =
        tx.settings("settings")?.unwrap_or(Value::Null)["singleWindow"]["customsCooDefaults"]
            .clone();
    let today = service.clock.now().map_err(unavailable)?.today.to_string();
    let mut defaults = mapping::defaults(
        business,
        &invoice,
        &exporter,
        &customer,
        &catalog,
        &preferences,
        &today,
    );
    let stored = tx.find_identity(business.kind(), &id.to_string())?;
    let mut current = defaults.clone();
    let mut locks = BTreeSet::new();
    if let Some(saved) = &stored {
        locks = serde_json::from_value(saved["_locks"].clone())
            .map_err(|_| unavailable("单证锁定状态损坏。"))?;
        if !saved["_baseline"].is_object() {
            return Err(unavailable("单证来源基线损坏。"));
        }
        draft::restore_locked(business, &mut current, saved, &locks);
        let (count, summary) = draft::changes(business, &saved["_baseline"], &defaults);
        current["sourceDiffCount"] = json!(count);
        current["sourceDiffSummary"] = json!(summary);
        for key in ["id", "draftRevision", "expectedDraftRevision"] {
            defaults[key] = current[key].clone();
        }
    }
    stamp(&mut current);
    stamp(&mut defaults);
    Ok(Loaded {
        current,
        defaults,
        stored,
        locks,
    })
}
pub fn save(
    tx: &Connection,
    actor: &Actor,
    service: &NativeService,
    business: Business,
    invoice_id: i64,
    body: &Value,
    loaded: &Loaded,
) -> Result<Value> {
    validation::structure(business, body).map_err(invalid)?;
    let invoice = source(tx, actor, invoice_id, "edit")?;
    if body["sourceInvoiceId"]
        .as_i64()
        .is_some_and(|n| n > 0 && n != invoice_id)
    {
        return Err(invalid("来源发票编号与请求路径不一致。"));
    }
    let revision = loaded
        .stored
        .as_ref()
        .and_then(|r| r["draftRevision"].as_i64())
        .unwrap_or(0);
    if body["expectedDraftRevision"].as_i64() != Some(revision) {
        return Err(super::super::error::conflict(
            "单证草稿已被修改，请重新读取后合并；当前修改已保留。",
        ));
    }
    let mut body = contracts::dto(contracts::schema(business.schema()), body.clone());
    let files = if business == Business::Coo {
        document_files::prepare(
            tx,
            loaded
                .stored
                .as_ref()
                .and_then(|v| v["id"].as_i64())
                .unwrap_or(0),
            &mut body,
        )?
    } else {
        Default::default()
    };
    for (key, value) in body.as_object_mut().into_iter().flatten() {
        if let Some(raw) = value.as_str() {
            if !key.starts_with('_') {
                *value = json!(raw.trim());
            }
        }
    }
    body["sourceInvoiceId"] = json!(invoice_id);
    body["invoiceNo"] = invoice["invoiceNo"].clone();
    body["contractNo"] = invoice["contractNo"].clone();
    body["status"] = json!("Draft");
    body["draftRevision"] = json!(
        revision
            .checked_add(1)
            .ok_or_else(|| unavailable("单证版本超出范围。"))?
    );
    body["expectedDraftRevision"] = body["draftRevision"].clone();
    body["lastGeneratedAt"] = json!(store::timestamp());
    if business == Business::Coo {
        let today = service.clock.now().map_err(unavailable)?.today.to_string();
        body["certNo"] = json!(mapping::cert_no(
            rules::text(&body, "certNo"),
            rules::text(&body, "certType"),
            rules::text(&body, "ciqRegNo"),
            rules::text(&body, "aplRegNo"),
            rules::text(&body, "aplDate"),
            &today
        ));
    } else {
        body["consignNo"] = loaded
            .stored
            .as_ref()
            .map(|r| r["consignNo"].clone())
            .unwrap_or_else(|| json!(""));
        body["counterpartyStatus"] = loaded
            .stored
            .as_ref()
            .map(|r| r["counterpartyStatus"].clone())
            .unwrap_or_else(|| json!(""));
    }
    let locks = draft::locks(business, &body, &loaded.defaults);
    body["_locks"] = json!(locks);
    body["manualLockedFieldCount"] = json!(locks.len());
    body["_baseline"] = loaded.defaults.clone();
    body["sourceDiffCount"] = json!(0);
    body["sourceDiffSummary"] = json!("");
    body["companyScope"] = invoice["companyScope"].clone();
    body["departmentId"] = invoice["departmentId"].clone();
    body["ownerUserId"] = invoice["ownerUserId"].clone();
    if let Some(saved) = &loaded.stored {
        body["expectedVersion"] = saved["versionNumber"].clone();
    }
    stamp(&mut body);
    let id = loaded
        .stored
        .as_ref()
        .and_then(|r| r["id"].as_i64())
        .unwrap_or(0);
    let saved = store::save_in_scope(
        tx,
        business.kind(),
        id,
        body,
        Some(invoice_id.to_string()),
        actor,
        if id == 0 { "create" } else { "edit" },
        Some(&invoice),
    )?;
    document_files::persist(tx, &saved, &files)?;
    if business == Business::Coo {
        references::remember(tx, actor, &saved)?;
    }
    Ok(contracts::dto(contracts::schema(business.schema()), saved))
}
