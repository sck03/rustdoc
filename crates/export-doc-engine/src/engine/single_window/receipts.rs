use super::*;
use export_doc_single_window::{Package, digest, package::describe};
use export_doc_storage::BlobWrite;
use std::collections::{BTreeMap, BTreeSet};

pub fn parse(
    service: &NativeService,
    business: Business,
    bytes: &[u8],
    name: &str,
) -> Result<Value> {
    let (mut value, time) =
        export_doc_single_window::receipt::parse(business, bytes, name).map_err(invalid)?;
    if let Some(raw) = time {
        let instant = if let Ok(time) = chrono::DateTime::parse_from_rfc3339(&raw) {
            time.with_timezone(&chrono::Utc)
        } else {
            let local = ["%Y%m%d%H%M%S", "%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M:%S"]
                .iter()
                .find_map(|format| chrono::NaiveDateTime::parse_from_str(&raw, format).ok())
                .ok_or_else(|| invalid("回执时间格式无效。"))?;
            service.clock.resolve_local(local).map_err(invalid)?
        };
        value["occurredAt"] = json!(instant.to_rfc3339());
    }
    Ok(value)
}

fn rank(status: &str) -> u8 {
    match status {
        "Approved" | "Rejected" => 5,
        "Failed" => 4,
        "PendingReview" => 3,
        "Accepted" => 2,
        "Received" => 1,
        _ => 0,
    }
}

fn primary(receipts: &[Value]) -> Result<&Value> {
    let terminals: BTreeSet<_> = receipts
        .iter()
        .map(|r| rules::text(r, "businessStatus"))
        .filter(|s| ["Approved", "Rejected"].contains(s))
        .collect();
    if terminals.len() > 1 {
        return Err(invalid(
            "同一回执包同时包含通过和退回终态，未写入业务状态。",
        ));
    }
    let time = |value: &Value| {
        value["occurredAt"]
            .as_str()
            .and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok())
    };
    receipts
        .iter()
        .max_by(|a, b| {
            rank(rules::text(a, "businessStatus"))
                .cmp(&rank(rules::text(b, "businessStatus")))
                .then_with(|| time(a).cmp(&time(b)))
        })
        .ok_or_else(|| invalid("回执包没有有效业务回执。"))
}

fn should_update(row: &Value, next: &Value) -> Result<bool> {
    let previous = rules::text(row, "status");
    let candidate = rules::text(next, "businessStatus");
    if ["Approved", "Rejected"].contains(&previous) && candidate != previous {
        return Ok(false);
    }
    if rank(previous) != rank(candidate) {
        return Ok(rank(candidate) > rank(previous));
    }
    let time = |s: &str| {
        chrono::DateTime::parse_from_rfc3339(s).map_err(|_| unavailable("已保存的回执时间损坏。"))
    };
    match (row["lastReceiptAt"].as_str(), next["occurredAt"].as_str()) {
        (Some(old), Some(new)) => Ok(time(new)? >= time(old)?),
        _ => Ok(true),
    }
}

pub fn references(receipts: &[Value], expected: &str) -> Result<String> {
    let refs: BTreeSet<_> = receipts
        .iter()
        .map(|r| rules::text(r, "referenceNo"))
        .filter(|s| !s.is_empty())
        .map(str::to_uppercase)
        .collect();
    if refs.len() > 1 {
        return Err(invalid("所选回执包含多个官方业务编号，不能归入同一批次。"));
    }
    let reference = refs.into_iter().next().unwrap_or_default();
    if !expected.is_empty() && !reference.is_empty() && !reference.eq_ignore_ascii_case(expected) {
        return Err(invalid("回执官方编号与批次不一致。"));
    }
    Ok(reference)
}

pub fn import(
    service: &NativeService,
    tx: &Connection,
    actor: &Actor,
    package: &Package,
) -> Result<(Value, Vec<Value>, usize)> {
    let m = &package.manifest;
    let mut row = tracking::find(tx, actor, rules::text(m, "batchReference"), "operate")?;
    tracking::verify(service, &row, m, true)?;
    let business = Business::parse(rules::text(m, "businessType")).map_err(invalid)?;
    let mut parsed = Vec::new();
    for (name, bytes) in &package.files {
        crate::operation::check()?;
        parsed.push(parse(service, business, bytes, name)?);
    }
    let reference = references(&parsed, rules::text(&row, "referenceNo"))?;
    if !reference.eq_ignore_ascii_case(rules::text(m, "receiptReferenceNo")) {
        return Err(invalid("回执内容编号与交接包清单不一致。"));
    }
    let next = primary(&parsed)?;
    let mut hashes: BTreeSet<String> = serde_json::from_value(row["_receiptHashes"].clone())
        .map_err(|_| unavailable("回执归档索引损坏。"))?;
    let mut count = 0;
    for ((_, bytes), receipt) in package.files.iter().zip(&parsed) {
        let hash = digest(bytes);
        if !hashes.insert(hash.clone()) {
            continue;
        }
        if hashes.len() > 5000 {
            return Err(invalid("该批次回执数量已达上限。"));
        }
        let mut record = receipt.clone();
        record["importedAt"] = json!(store::timestamp());
        row["receiptRecords"]
            .as_array_mut()
            .ok_or_else(|| unavailable("回执历史损坏。"))?
            .push(record);
        tx.insert_blob(&BlobWrite {
            kind: &format!("sw-receipt:{hash}"),
            record_id: row["id"].as_i64().unwrap_or(0),
            file_name: rules::text(receipt, "sourceFileName"),
            media_type: "application/xml",
            digest: &hash,
            content: bytes,
            created_at: &store::timestamp(),
        })?;
        count += 1;
    }
    let changed = tracking::add_package(&mut row, m, "Imported")?;
    if count > 0 || changed {
        row["_receiptHashes"] = json!(hashes);
        row["receiptCount"] = json!(hashes.len());
        if should_update(&row, next)? {
            let status = rules::text(next, "businessStatus");
            row["status"] = json!(if status == "Unknown" {
                "ReceiptImported"
            } else {
                status
            });
            if !reference.is_empty() {
                row["referenceNo"] = json!(reference);
            }
            row["lastReceiptCode"] = next["receiptCode"].clone();
            row["lastReceiptMessage"] = next["receiptMessage"].clone();
            row["lastReceiptAt"] = next["occurredAt"]
                .as_str()
                .map(|s| json!(s))
                .unwrap_or_else(|| json!(store::timestamp()));
            write_back(tx, actor, &row, business, next)?;
        }
        row = tracking::save(tx, actor, row, None)?;
    }
    Ok((row, parsed, count))
}

fn write_back(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    business: Business,
    receipt: &Value,
) -> Result<()> {
    if row["_sourceLocal"] != true {
        return Ok(());
    }
    let m = &row["_manifest"];
    let Some(mut document) =
        tx.get(business.kind(), m["sourceDocumentId"].as_i64().unwrap_or(0))?
    else {
        return Ok(());
    };
    if document["sourceInvoiceId"] != m["sourceInvoiceId"]
        || document["companyScope"] != m["companyScope"]
    {
        return Err(unavailable("申报来源关联不一致，已停止回写。"));
    }
    let newer = store::all(tx, BATCHES)?.iter().any(|r| {
        r["_sourceLocal"] == true
            && r["sourceInvoiceId"] == m["sourceInvoiceId"]
            && r["businessType"] == business.name()
            && r["submissionVersion"].as_i64() > m["submissionVersion"].as_i64()
    });
    if document["draftRevision"] != m["draftRevision"] || newer {
        return Ok(());
    }
    document["status"] = row["status"].clone();
    let reference = rules::text(receipt, "referenceNo");
    if !reference.is_empty() {
        document[if business == Business::Coo {
            "certNo"
        } else {
            "consignNo"
        }] = json!(reference);
    }
    if business == Business::Acd {
        document["counterpartyStatus"] = receipt["businessStatus"].clone();
    }
    document["expectedVersion"] = document["versionNumber"].clone();
    let id = document["id"].as_i64().unwrap_or(0);
    store::save(
        tx,
        business.kind(),
        id,
        document,
        Some(m["sourceInvoiceId"].to_string()),
        actor,
        "receipt",
    )?;
    Ok(())
}

pub fn export(service: &NativeService, actor: &Actor, body: &Value) -> Result<(Package, Value)> {
    profiles::local(service)?;
    let selected: Vec<String> = serde_json::from_value(body["receiptFiles"].clone())
        .map_err(|_| invalid("请选择 XML 回执文件。"))?;
    if selected.is_empty() || selected.len() > 200 {
        return Err(invalid("每次请选择 1–200 个回执文件。"));
    }
    service.store.transaction(|tx| {
        let mut row = tracking::find(tx, actor, rules::text(body, "batchReference"), "operate")?;
        if row["businessType"] != body["businessType"] || row["invoiceNo"] != body["invoiceNo"] {
            return Err(invalid("回执导出业务或发票不匹配。"));
        }
        let profile = profiles::active(tx, actor, service)?;
        for (mkey, pkey) in [
            ("stationKey", "stationKey"),
            ("clientProfileKey", "profileKey"),
            ("companyScope", "companyScope"),
            ("cardIdentifier", "cardIdentifier"),
        ] {
            if row["_manifest"][mkey] != profile[pkey] {
                return Err(invalid("请切换到该批次绑定的持卡机档案。"));
            }
        }
        let business = Business::parse(rules::text(&row, "businessType")).map_err(invalid)?;
        let mut files = BTreeMap::new();
        let mut descriptors = Vec::new();
        let mut parsed = Vec::new();
        let mut total = 0;
        let mut hashes = BTreeSet::new();
        for (index, path) in selected.iter().enumerate() {
            crate::operation::check()?;
            let path = std::path::Path::new(path);
            let name = path
                .file_name()
                .and_then(|s| s.to_str())
                .filter(|n| crate::paths::valid_file_name(n))
                .ok_or_else(|| invalid("回执文件名无效。"))?;
            let bytes = super::super::media::read_local(path, 10 * 1024 * 1024)?;
            total += bytes.len();
            if total > 50 * 1024 * 1024 {
                return Err(invalid("回执总容量最多 50 MiB。"));
            }
            if !hashes.insert(digest(&bytes)) {
                continue;
            }
            let name = format!("receipts/{:03}_{name}", index + 1);
            parsed.push(parse(service, business, &bytes, &name)?);
            descriptors
                .push(describe(&name, &bytes, "application/xml", "官方业务回执").map_err(invalid)?);
            files.insert(name, bytes);
        }
        primary(&parsed)?;
        let reference = references(&parsed, rules::text(&row, "referenceNo"))?;
        let mut manifest = row["_manifest"].clone();
        manifest["packageType"] = json!("ReceiptPackage");
        manifest["packageId"] = json!(crate::paths::nonce().map_err(unavailable)?.to_uppercase());
        manifest["sourcePackageDigest"] = manifest["contentDigest"].clone();
        manifest["snapshotSha256"] = json!("");
        manifest["receiptReferenceNo"] = json!(reference);
        manifest["payloadFiles"] = json!(descriptors);
        manifest["attachmentFiles"] = json!([]);
        manifest["warnings"] = json!([]);
        manifest["createdAt"] = json!(store::timestamp());
        let package = Package::seal(
            manifest,
            files,
            &tracking::secret(service, &row)?,
            &tracking::check,
        )
        .map_err(invalid)?;
        tracking::add_package(&mut row, &package.manifest, "Exported")?;
        if rank(rules::text(&row, "status")) == 0 {
            row["status"] = json!("ReceiptPackageExported");
        }
        let row = tracking::save(tx, actor, row, None)?;
        Ok((package, row))
    })
}
