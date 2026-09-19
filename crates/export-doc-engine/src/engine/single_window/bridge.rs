use super::*;
use crate::paths::{ensure_safe_absolute, nonce};
use std::{collections::BTreeSet, fs, io::Write, path::Path};

pub const OPERATIONS: &[Operation] = &[
    DISPATCH_SINGLE_WINDOW_BATCH_TO_CLIENT,
    COLLECT_SINGLE_WINDOW_CLIENT_RECEIPTS,
];

fn binding(
    service: &NativeService,
    tx: &Connection,
    actor: &Actor,
    row: &Value,
) -> Result<(Value, Business)> {
    let profile = profiles::active(tx, actor, service)?;
    for (mk, pk) in [
        ("stationKey", "stationKey"),
        ("clientProfileKey", "profileKey"),
        ("cardIdentifier", "cardIdentifier"),
        ("companyScope", "companyScope"),
    ] {
        if row["_manifest"][mk] != profile[pk] {
            return Err(error(409, "请先切换到批次绑定的公司及操作卡档案。"));
        }
    }
    let business = Business::parse(rules::text(row, "businessType")).map_err(invalid)?;
    if profile[if business == Business::Coo {
        "canSubmitCustomsCoo"
    } else {
        "canSubmitAgentConsignment"
    }] != true
    {
        return Err(error(403, "当前档案未启用此申报业务。"));
    }
    Ok((profile, business))
}

fn directory(
    service: &NativeService,
    profile: &Value,
    business: Business,
) -> Result<std::path::PathBuf> {
    profiles::root(
        service,
        rules::text(
            profile,
            if business == Business::Coo {
                "customsCooClientRootPath"
            } else {
                "agentConsignmentClientRootPath"
            },
        ),
    )
}

/// Publish a complete file without ever replacing an existing official payload.
fn publish(path: &Path, bytes: &[u8]) -> Result<()> {
    ensure_safe_absolute(path).map_err(invalid)?;
    let temporary = path.with_extension(format!("{}.tmp", nonce().map_err(unavailable)?));
    let result = (|| {
        let mut file = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&temporary)?;
        file.write_all(bytes)?;
        file.sync_all()?;
        drop(file);
        crate::operation::check()?;
        ensure_safe_absolute(path).map_err(invalid)?;
        fs::hard_link(&temporary, path).map_err(|e| {
            if e.kind() == std::io::ErrorKind::AlreadyExists {
                error(409, "客户端目录已有同名报文，未覆盖原文件。")
            } else {
                unavailable(format!("发布客户端报文失败：{e}"))
            }
        })?;
        Ok(())
    })();
    if temporary.try_exists().unwrap_or(false) {
        fs::remove_file(&temporary)?;
    }
    result
}

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    profiles::local(service)?;
    let id = body["batchId"]
        .as_i64()
        .filter(|id| *id > 0)
        .ok_or_else(|| invalid("请选择有效批次。"))?;
    if operation == COLLECT_SINGLE_WINDOW_CLIENT_RECEIPTS {
        return collect(service, actor, id);
    }
    let (row, profile, outbox, package) = service.store.transaction(|tx| {
        let mut row = tracking::get(tx, actor, id, "operate")?;
        let (profile, business) = binding(service, tx, actor, &row)?;
        let outbox = directory(service, &profile, business)?.join("OutBox");
        let package = tracking::archived(tx, &row)?;
        tracking::verify(service, &row, &package.manifest, false)?;
        if row["status"] == "QueuedToClient" {
            return Ok((row, profile, outbox, package));
        }
        if row["clientDispatchAttemptCount"].as_i64().unwrap_or(0) > 0 {
            return Err(error(
                409,
                "此批次已经尝试派发；请核对客户端收件状态后新建提交版本，避免重复申报。",
            ));
        }
        if row["status"] != "SubmitPackageImported" {
            return Err(error(
                409,
                "请先导入提交包并确认当前持卡机，再送入官方客户端。",
            ));
        }
        row["status"] = json!("ClientDispatching");
        row["clientDispatchOperationId"] = json!(nonce().map_err(unavailable)?);
        row["clientDispatchAttemptCount"] = json!(1);
        row["clientDispatchLeaseUntil"] =
            json!((chrono::Utc::now() + chrono::Duration::seconds(60)).to_rfc3339());
        let row = tracking::save(tx, actor, row, None)?;
        Ok((row, profile, outbox, package))
    })?;
    if row["status"] != "QueuedToClient" {
        let result = (|| -> Result<()> {
            ensure_safe_absolute(&outbox).map_err(invalid)?;
            fs::create_dir_all(&outbox)?;
            for descriptor in package.manifest["payloadFiles"]
                .as_array()
                .into_iter()
                .flatten()
            {
                crate::operation::check()?;
                let name = rules::text(descriptor, "relativePath");
                let file = Path::new(name)
                    .file_name()
                    .ok_or_else(|| invalid("报文文件名无效。"))?;
                let content = package
                    .files
                    .get(name)
                    .ok_or_else(|| unavailable("报文内容缺失。"))?;
                publish(&outbox.join(file), content)?;
            }
            Ok(())
        })();
        // The attempt marker survives cancellation or an uncertain partial handoff.
        // A subsequent request cannot automatically replay an official submission.
        let finalize = crate::operation::OperationScope::new(std::time::Duration::from_secs(10));
        finalize.run(|| {
            service.store.transaction(|tx| {
                let mut current = tracking::get(tx, actor, id, "operate")?;
                current["clientDispatchLeaseUntil"] = Value::Null;
                current["status"] = json!(if result.is_ok() {
                    "QueuedToClient"
                } else {
                    "ClientDispatchFailed"
                });
                if result.is_ok() {
                    current["lastClientDispatchAt"] = json!(store::timestamp());
                } else {
                    current["_dispatchError"] =
                        json!("派发未完整确认；请核对官方客户端后重新生成提交版本。");
                }
                tracking::save(tx, actor, current, None).map(|_| ())
            })
        })?;
        result?;
    }
    Ok(
        json!({"batchId":id,"batchReference":row["batchReference"],"targetDirectory":outbox,"profileName":profile["profileName"],"payloadFileCount":row["payloadFileCount"],"attachmentFileCount":row["attachmentFileCount"]}),
    )
}

fn collect(service: &NativeService, actor: &Actor, id: i64) -> Result<Value> {
    let (row, root, business) = service.store.transaction(|tx| {
        let row = tracking::get(tx, actor, id, "operate")?;
        let (profile, business) = binding(service, tx, actor, &row)?;
        let root = directory(service, &profile, business)?;
        Ok((row, root, business))
    })?;
    let mut selected = BTreeSet::new();
    let mut inspected = 0;
    for name in ["InBox", "FailBox"] {
        let folder = root.join(name);
        ensure_safe_absolute(&folder).map_err(invalid)?;
        if !folder.try_exists()? {
            continue;
        }
        for item in fs::read_dir(&folder)? {
            crate::operation::check()?;
            inspected += 1;
            if inspected > 10_000 {
                return Err(invalid("客户端回执目录超过 10000 项，请先归档已处理文件。"));
            }
            let path = item?.path();
            ensure_safe_absolute(&path).map_err(invalid)?;
            if !path.is_file()
                || !path
                    .extension()
                    .is_some_and(|s| s.eq_ignore_ascii_case("xml"))
            {
                continue;
            }
            let name = path
                .file_name()
                .and_then(|s| s.to_str())
                .ok_or_else(|| invalid("回执文件名编码无效。"))?;
            let reference = rules::text(&row, "referenceNo");
            let correlated = name
                .to_ascii_uppercase()
                .contains(&rules::text(&row, "batchReference").to_ascii_uppercase());
            let bytes = super::super::media::read_local(&path, 10 * 1024 * 1024)?;
            let parsed = match receipts::parse(service, business, &bytes, name) {
                Ok(value) => value,
                Err(cause) if correlated => return Err(cause),
                Err(_) => continue,
            };
            if correlated
                || !reference.is_empty()
                    && rules::text(&parsed, "referenceNo").eq_ignore_ascii_case(reference)
            {
                selected.insert(path);
                if selected.len() > 200 {
                    return Err(invalid("匹配回执超过 200 份，请分批选择导出。"));
                }
            }
        }
    }
    Ok(
        json!({"batchId":id,"batchReference":row["batchReference"],"receiptRootPath":root,"receiptFiles":selected}),
    )
}
