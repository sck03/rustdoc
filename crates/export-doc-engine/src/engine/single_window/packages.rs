use super::*;
use crate::paths::{atomic_write, ensure_safe_absolute, nonce};
use export_doc_single_window::{Package, authentication, digest, package::describe};
use std::{collections::BTreeMap, path::Path};

pub const OPERATIONS: &[Operation] = &[
    DOWNLOAD_CUSTOMS_COO_SUBMIT_PACKAGE,
    DOWNLOAD_AGENT_CONSIGNMENT_SUBMIT_PACKAGE,
    SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,
    SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH,
    DOWNLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
    SAVE_SINGLE_WINDOW_RECEIPT_PACKAGE_TO_PATH,
    IMPORT_SINGLE_WINDOW_SUBMIT_PACKAGE,
    IMPORT_SINGLE_WINDOW_RECEIPT_PACKAGE,
    UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
    UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
];
pub const LOCAL: &[Operation] = &[
    SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,
    SAVE_AGENT_CONSIGNMENT_SUBMIT_PACKAGE_TO_PATH,
    DOWNLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
    SAVE_SINGLE_WINDOW_RECEIPT_PACKAGE_TO_PATH,
    IMPORT_SINGLE_WINDOW_SUBMIT_PACKAGE,
    IMPORT_SINGLE_WINDOW_RECEIPT_PACKAGE,
    UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
];
pub const UPLOADS: &[Operation] = &[
    UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
    UPLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
];
pub const POLICY: &str = "申报快照、提交包及回执保存在当前业务数据库，附件随备份恢复。";

fn manifest(
    business: Business,
    doc: &Value,
    invoice: &Value,
    assignment: &Value,
    version: i64,
) -> Result<Value> {
    let mut m = contracts::initial(contracts::schema("SingleWindowPackageManifest"));
    m["schemaVersion"] = json!("5.0");
    m["packageId"] = json!(nonce().map_err(unavailable)?.to_uppercase());
    m["batchReference"] = json!(format!(
        "SW-{}",
        nonce().map_err(unavailable)?.to_uppercase()
    ));
    m["packageType"] = json!("SubmitPackage");
    m["businessType"] = json!(business.name());
    m["sourceId"] = invoice["id"].clone();
    m["sourceDocumentId"] = doc["id"].clone();
    m["sourceDocumentType"] = json!(if business == Business::Coo {
        "CustomsCooDocument"
    } else {
        "AgentConsignmentDocument"
    });
    m["submissionVersion"] = json!(version);
    m["draftRevision"] = doc["draftRevision"].clone();
    m["sourceBaselineHash"] = json!(digest(&serde_json::to_vec(invoice)?));
    for key in ["invoiceNo", "companyScope"] {
        m[key] = invoice[key].clone();
    }
    m["contractNo"] = doc["contractNo"].clone();
    for key in ["stationKey", "cardIdentifier"] {
        m[key] = assignment[key].clone();
    }
    m["clientProfileKey"] = assignment["profileKey"].clone();
    m["clientProfileName"] = assignment["profileName"].clone();
    m["assignmentNonce"] = json!(nonce().map_err(unavailable)?.to_uppercase());
    m["authenticationAlgorithm"] = json!("HMAC-SHA256");
    m["createdAt"] = json!(store::timestamp());
    m["createdOnMachine"] = assignment["stationKey"].clone();
    Ok(m)
}

fn submit(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    business: Business,
    id: i64,
    body: &Value,
) -> Result<Export> {
    // Preserve the existing implicit first draft, but do not hold its transaction
    // while reading attachments or constructing the archive.
    let initial = service.store.read_for(actor, operation, &[], |tx, actor| {
        documents::load(tx, actor, service, business, id, "operate")
    })?;
    if initial.stored.is_none() {
        service
            .store
            .transaction_for(actor, operation, &[], |tx, actor| {
                let loaded = documents::load(tx, actor, service, business, id, "operate")?;
                if loaded.stored.is_none() {
                    documents::save(tx, actor, service, business, id, &loaded.current, &loaded)?;
                }
                Ok(())
            })?;
    }
    let (invoice, loaded, assignment, version, attachments) =
        service.store.read_for(actor, operation, &[], |tx, actor| {
            let invoice = source(tx, actor, id, "operate")?;
            let loaded = documents::load(tx, actor, service, business, id, "view")?;
            let document = &loaded.current;
            let issues = rules::validation::review(business, document);
            if !issues.is_empty() {
                return Err(invalid(format!(
                    "导出前请完成申报校验：{}",
                    issues
                        .iter()
                        .take(8)
                        .map(|i| i.message.as_str())
                        .collect::<Vec<_>>()
                        .join("；")
                )));
            }
            let assignment = if rules::text(body, "stationAssignmentCode").is_empty() {
                profiles::assignment(service, &profiles::active(tx, actor, service)?)?
            } else {
                authentication::assignment(rules::text(body, "stationAssignmentCode"))
                    .map_err(invalid)?
            };
            if assignment["companyScope"] != invoice["companyScope"]
                || assignment[if business == Business::Coo {
                    "canSubmitCustomsCoo"
                } else {
                    "canSubmitAgentConsignment"
                }] != true
            {
                return Err(invalid("授权码的公司抬头或申报业务与申报资料不一致。"));
            }
            let version = tx
                .single_window_submission_version(id, business.name())?
                .checked_add(1)
                .ok_or_else(|| unavailable("申报版本超限。"))?;
            let mut attachments = Vec::new();
            let mut total = 0_u64;
            for row in document["attachments"].as_array().into_iter().flatten() {
                crate::operation::check()?;
                let document_id = document["id"]
                    .as_i64()
                    .ok_or_else(|| unavailable("申报单证编号损坏。"))?;
                let key = rules::text(row, "filePath");
                let info = tx
                    .blob_info(document_id, key)?
                    .ok_or_else(|| error(404, "单证附件不存在，请重新选择并保存。"))?;
                total = total
                    .checked_add(info.size)
                    .ok_or_else(|| invalid("附件容量超限。"))?;
                if total > 50 * 1024 * 1024 {
                    return Err(invalid("附件总容量最多 50 MiB。"));
                }
                let blob = tx
                    .blob(document_id, key)?
                    .ok_or_else(|| unavailable("单证附件内容缺失。"))?;
                attachments.push((row.clone(), blob));
            }
            Ok((invoice, loaded, assignment, version, attachments))
        })?;
    let document = &loaded.current;
    let mut m = manifest(business, document, &invoice, &assignment, version)?;
    let mut files = BTreeMap::new();
    let snapshot =
        serde_json::to_vec_pretty(&export_doc_single_window::package::wire(document, true))?;
    m["snapshotSha256"] = json!(digest(&snapshot));
    files.insert("snapshot.json".into(), snapshot);
    let payload = rules::xml::payload(business, document).map_err(invalid)?;
    let name = format!(
        "payloads/{}_{}.xml",
        rules::text(&m, "batchReference"),
        business.scope()
    );
    let mut payloads =
        vec![describe(&name, &payload, "application/xml", "申报业务报文").map_err(invalid)?];
    files.insert(name, payload);
    let mut descriptors = Vec::new();
    for (index, (row, blob)) in attachments.into_iter().enumerate() {
        crate::operation::check()?;
        if digest(&blob.content) != blob.digest {
            return Err(unavailable("附件摘要校验失败。"));
        }
        let name = format!(
            "attachments/{:03}_{}",
            index + 1,
            rules::text(&row, "fileName")
        );
        descriptors.push(
            describe(
                &name,
                &blob.content,
                rules::text(&row, "mediaType"),
                rules::text(&row, "description"),
            )
            .map_err(invalid)?,
        );
        let payload = document_files::payload(document, &row, &blob.content);
        let payload_name = format!(
            "payloads/{}_attachment_{:03}.xml",
            rules::text(&m, "batchReference"),
            index + 1
        );
        payloads.push(
            describe(
                &payload_name,
                &payload,
                "application/xml",
                "原产地证附件报文",
            )
            .map_err(invalid)?,
        );
        files.insert(payload_name, payload);
        files.insert(name, blob.content);
    }
    m["payloadFiles"] = json!(payloads);
    m["attachmentFiles"] = json!(descriptors);
    let secret = rules::text(&assignment, "authenticationSecret");
    let package = Package::seal(m, files, secret, &tracking::check).map_err(invalid)?;
    let bytes = package.encode(&tracking::check).map_err(invalid)?;
    let archive_hash = digest(&bytes);
    let manifest = package.manifest;
    let row = service
        .store
        .transaction_for(actor, operation, &[], |tx, actor| {
            let current = documents::load(tx, actor, service, business, id, "operate")?;
            if source(tx, actor, id, "operate")? != invoice
                || current.current != loaded.current
                || current.stored != loaded.stored
                || tx.single_window_submission_version(id, business.name())? != version - 1
            {
                return Err(error(409, "申报来源、草稿或提交版本已变化，请重新导出。"));
            }
            if rules::text(body, "stationAssignmentCode").is_empty()
                && profiles::assignment(service, &profiles::active(tx, actor, service)?)?
                    != assignment
            {
                return Err(error(409, "持卡机档案已变化，请重新导出。"));
            }
            let mut row = tracking::from_manifest(&manifest, true);
            row["referenceNo"] = document[if business == Business::Coo {
                "certNo"
            } else {
                "consignNo"
            }]
            .clone();
            row["status"] = json!("SubmitPackageExported");
            tracking::bind(service, &mut row, secret)?;
            tracking::add_package(&mut row, &manifest, "Exported")?;
            let row = tracking::save(tx, actor, row, Some(&invoice))?;
            tracking::archive(tx, &row, &bytes, &archive_hash)?;
            Ok(row)
        })?;
    Ok(Export {
        manifest,
        bytes,
        row,
    })
}

pub(super) struct Export {
    pub manifest: Value,
    pub bytes: Vec<u8>,
    pub row: Value,
}

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Vec<u8>> {
    export_doc_contracts::validation::structure(contracts::request(operation.id), body)
        .map_err(invalid)?;
    if [
        IMPORT_SINGLE_WINDOW_SUBMIT_PACKAGE,
        IMPORT_SINGLE_WINDOW_RECEIPT_PACKAGE,
    ]
    .contains(&operation)
    {
        profiles::local(service)?;
        let path = Path::new(rules::text(body, "packagePath"));
        let bytes =
            super::super::media::read_local(path, export_doc_single_window::package::MAX_PACKAGE)?;
        let name = path
            .file_name()
            .and_then(|s| s.to_str())
            .ok_or_else(|| invalid("请选择交接包文件。"))?;
        let result = imports::import(service, actor, operation, body, name, &bytes)?;
        return serde_json::to_vec(&result).map_err(Into::into);
    }
    let download = [
        DOWNLOAD_CUSTOMS_COO_SUBMIT_PACKAGE,
        DOWNLOAD_AGENT_CONSIGNMENT_SUBMIT_PACKAGE,
        DOWNLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
    ]
    .contains(&operation);
    let path = Path::new(rules::text(body, "packagePath"));
    if !download {
        profiles::local(service)?;
        ensure_safe_absolute(path).map_err(invalid)?;
        if !path
            .extension()
            .is_some_and(|s| s.eq_ignore_ascii_case("swpkg"))
        {
            return Err(invalid("请选择 .swpkg 交接包保存位置。"));
        }
    }
    let receipt = [
        DOWNLOAD_SINGLE_WINDOW_RECEIPT_PACKAGE,
        SAVE_SINGLE_WINDOW_RECEIPT_PACKAGE_TO_PATH,
    ]
    .contains(&operation);
    let Export {
        manifest,
        bytes,
        row,
    } = if receipt {
        receipts::export(service, actor, operation, body)?
    } else {
        let business = if [
            DOWNLOAD_CUSTOMS_COO_SUBMIT_PACKAGE,
            SAVE_CUSTOMS_COO_SUBMIT_PACKAGE_TO_PATH,
        ]
        .contains(&operation)
        {
            Business::Coo
        } else {
            Business::Acd
        };
        let id = parameter(parameters, "sourceId")
            .parse()
            .ok()
            .filter(|id| *id > 0)
            .ok_or_else(|| invalid("申报资料编号无效。"))?;
        submit(service, actor, operation, business, id, body)?
    };
    if download {
        return Ok(bytes);
    }
    crate::operation::check()?;
    atomic_write(path, &bytes).map_err(unavailable)?;
    serde_json::to_vec(&json!({"success":true,"packagePath":path,"manifest":manifest,"trackingBatchId":row["id"],"storagePolicy":POLICY,"message":"交接包已归档并保存。"})).map_err(Into::into)
}
