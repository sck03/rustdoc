use super::*;
use export_doc_single_window::{Package, authentication, digest};
use export_doc_storage::BlobWrite;

pub fn check() -> std::result::Result<(), String> {
    crate::operation::check().map_err(|e| e.message)
}

pub fn get(tx: &Connection, actor: &Actor, id: i64, action: &str) -> Result<Value> {
    let value = store::get(tx, BATCHES, id)?;
    if !auth::visible(actor, PERMISSION, action, &value) {
        return Err(error(403, "无权操作该申报批次。"));
    }
    Ok(value)
}

pub fn find(tx: &Connection, actor: &Actor, reference: &str, action: &str) -> Result<Value> {
    let row = tx
        .find_identity(BATCHES, &store::normalize(reference))?
        .ok_or_else(|| error(404, "交接包对应的申报批次不存在。"))?;
    get(
        tx,
        actor,
        row["id"]
            .as_i64()
            .ok_or_else(|| unavailable("批次编号损坏。"))?,
        action,
    )
}

pub fn save(
    tx: &Connection,
    actor: &Actor,
    mut row: Value,
    scope: Option<&Value>,
) -> Result<Value> {
    let id = row["id"].as_i64().unwrap_or(0);
    let identity = rules::text(&row, "batchReference").to_owned();
    if id > 0 {
        row["expectedVersion"] = row["versionNumber"].clone();
    }
    let mut row = store::save_in_scope(
        tx,
        BATCHES,
        id,
        row,
        Some(identity),
        actor,
        "handoff",
        scope,
    )?;
    row["batchId"] = row["id"].clone();
    tx.set_body(
        row["id"]
            .as_i64()
            .ok_or_else(|| unavailable("批次编号损坏。"))?,
        &row,
    )?;
    Ok(row)
}

pub fn from_manifest(m: &Value, local: bool) -> Value {
    let mut row = contracts::initial(contracts::schema("SingleWindowOperationCenterDetail"));
    for key in [
        "batchReference",
        "submissionVersion",
        "draftRevision",
        "businessType",
        "invoiceNo",
        "contractNo",
        "companyScope",
        "sourceInvoiceId",
        "sourceDocumentId",
    ] {
        row[key] = m[key].clone();
    }
    row["_manifest"] = m.clone();
    row["_sourceLocal"] = json!(local);
    row["clientProfileName"] = m["clientProfileName"].clone();
    row["assignedCardIdentifier"] = m["cardIdentifier"].clone();
    row["payloadFileCount"] = json!(m["payloadFiles"].as_array().map_or(0, Vec::len));
    row["attachmentFileCount"] = json!(m["attachmentFiles"].as_array().map_or(0, Vec::len));
    row["warningCount"] = json!(m["warnings"].as_array().map_or(0, Vec::len));
    row["lastReceiptCode"] = json!("");
    row["lastReceiptMessage"] = json!("");
    row["receiptCount"] = json!(0);
    row["_receiptHashes"] = json!([]);
    row
}

pub fn add_package(row: &mut Value, m: &Value, direction: &str) -> Result<bool> {
    let key = format!("{}:{direction}", rules::text(m, "packageId"));
    let mut ids: Vec<String> =
        serde_json::from_value(row.get("_packageIds").cloned().unwrap_or_else(|| json!([])))
            .map_err(|_| unavailable("交接历史损坏。"))?;
    if ids.contains(&key) {
        return Ok(false);
    }
    if ids.len() >= 5000 {
        return Err(invalid("该批次交接历史已达上限，请新建提交版本。"));
    }
    ids.push(key);
    row["_packageIds"] = json!(ids);
    row["packageRecords"].as_array_mut().ok_or_else(||unavailable("交接历史损坏。"))?.push(json!({
        "packageType":m["packageType"],"direction":direction,"payloadFileCount":m["payloadFiles"].as_array().map_or(0,Vec::len),
        "attachmentFileCount":m["attachmentFiles"].as_array().map_or(0,Vec::len),"warningCount":m["warnings"].as_array().map_or(0,Vec::len),"createdAt":store::timestamp()
    }));
    Ok(true)
}

pub fn archive(tx: &Connection, row: &Value, bytes: &[u8]) -> Result<()> {
    let id = row["id"]
        .as_i64()
        .ok_or_else(|| unavailable("批次编号损坏。"))?;
    tx.delete_blob(id, "sw-submit")?;
    tx.insert_blob(&BlobWrite {
        kind: "sw-submit",
        record_id: id,
        file_name: &format!("{}.zip", rules::text(row, "batchReference")),
        media_type: "application/zip",
        digest: &digest(bytes),
        content: bytes,
        created_at: &store::timestamp(),
    })?;
    Ok(())
}

pub fn archived(tx: &Connection, row: &Value) -> Result<Package> {
    let file = tx
        .blob(
            row["id"]
                .as_i64()
                .ok_or_else(|| unavailable("批次编号损坏。"))?,
            "sw-submit",
        )?
        .ok_or_else(|| unavailable("提交包归档缺失。"))?;
    if digest(&file.content) != file.digest {
        return Err(unavailable("提交包归档摘要不匹配。"));
    }
    Package::decode(&file.content, "SubmitPackage", &check).map_err(unavailable)
}

pub fn secret(service: &NativeService, row: &Value) -> Result<zeroize::Zeroizing<String>> {
    service
        .protector
        .unprotect(
            &format!("sw-batch:{}", rules::text(row, "batchReference")),
            rules::text(row, "_assignmentSecret"),
        )
        .map_err(unavailable)
}

pub fn bind(service: &NativeService, row: &mut Value, secret: &str) -> Result<()> {
    row["_assignmentSecret"] = json!(
        service
            .protector
            .protect(
                &format!("sw-batch:{}", rules::text(row, "batchReference")),
                secret
            )
            .map_err(unavailable)?
    );
    Ok(())
}

pub fn matches(row: &Value, m: &Value, receipt: bool) -> Result<()> {
    let expected = &row["_manifest"];
    for key in [
        "businessType",
        "batchReference",
        "sourceInvoiceId",
        "sourceDocumentId",
        "sourceDocumentType",
        "submissionVersion",
        "draftRevision",
        "sourceBaselineHash",
        "invoiceNo",
        "contractNo",
        "companyScope",
        "stationKey",
        "clientProfileKey",
        "cardIdentifier",
        "assignmentNonce",
    ] {
        if m[key] != expected[key] {
            return Err(invalid("交接包的来源版本、公司或操作卡与批次不一致。"));
        }
    }
    if m[if receipt {
        "sourcePackageDigest"
    } else {
        "contentDigest"
    }] != expected["contentDigest"]
    {
        return Err(invalid("交接包的原提交内容摘要不一致。"));
    }
    Ok(())
}

pub fn verify(service: &NativeService, row: &Value, m: &Value, receipt: bool) -> Result<()> {
    matches(row, m, receipt)?;
    authentication::verify(m, &secret(service, row)?).map_err(invalid)
}

pub fn list(tx: &Connection, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    let keyword = store::normalize(parameter(query, "keyword"));
    let business = parameter(query, "businessType");
    let status = parameter(query, "status");
    if !business.is_empty() {
        Business::parse(business).map_err(invalid)?;
    }
    if keyword.chars().count() > 200 {
        return Err(invalid("批次检索最多 200 字。"));
    }
    let mut rows = store::all(tx, BATCHES)?;
    rows.retain(|r| {
        auth::visible(actor, PERMISSION, "view", r)
            && (business.is_empty() || r["businessType"] == business)
            && (status.is_empty() || r["status"] == status)
            && ["batchReference", "invoiceNo", "contractNo", "referenceNo"]
                .iter()
                .any(|key| store::normalize(rules::text(r, key)).contains(&keyword))
    });
    rows.sort_by(|a, b| b["id"].as_i64().cmp(&a["id"].as_i64()));
    let mut page = store::page_only(rows, query);
    page["rows"] = page["items"].take();
    Ok(page)
}
