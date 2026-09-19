use super::*;
use export_doc_single_window::{Package, authentication};

pub fn import(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    metadata: &Value,
    name: &str,
    bytes: &[u8],
) -> Result<Value> {
    if !crate::paths::valid_file_name(name) {
        return Err(invalid("交接包文件名无效。"));
    }
    let submit = [
        UPLOAD_SINGLE_WINDOW_SUBMIT_PACKAGE,
        IMPORT_SINGLE_WINDOW_SUBMIT_PACKAGE,
    ]
    .contains(&operation);
    let package = Package::decode(
        bytes,
        if submit {
            "SubmitPackage"
        } else {
            "ReceiptPackage"
        },
        &tracking::check,
    )
    .map_err(invalid)?;
    if metadata["keepWorkingDirectory"] == true
        || !rules::text(metadata, "workingDirectory").is_empty()
    {
        return Err(invalid(
            "原生交接直接使用数据库归档，请关闭保留解包工作目录。",
        ));
    }
    let (row, parsed, count) = service.store.transaction(|tx| {
        if !submit {
            return receipts::import(service, tx, actor, &package);
        }
        let profile = profiles::active(tx, actor, service)?;
        let m = &package.manifest;
        for (field, profile_field) in [
            ("stationKey", "stationKey"),
            ("clientProfileKey", "profileKey"),
            ("companyScope", "companyScope"),
            ("cardIdentifier", "cardIdentifier"),
        ] {
            if m[field] != profile[profile_field] {
                return Err(invalid("提交包不是分配给当前公司、操作卡和持卡机档案。"));
            }
        }
        let business = Business::parse(rules::text(m, "businessType")).map_err(invalid)?;
        if profile[if business == Business::Coo {
            "canSubmitCustomsCoo"
        } else {
            "canSubmitAgentConsignment"
        }] != true
        {
            return Err(error(403, "当前档案未启用此申报业务。"));
        }
        authentication::verify(m, &profiles::secret(service, &profile)?).map_err(invalid)?;
        let previous =
            tx.find_identity(BATCHES, &store::normalize(rules::text(m, "batchReference")))?;
        let mut row = if let Some(row) = previous {
            let row = tracking::get(tx, actor, row["id"].as_i64().unwrap_or(0), "operate")?;
            tracking::matches(&row, m, false)?;
            row
        } else {
            tracking::from_manifest(m, false)
        };
        let changed = tracking::add_package(&mut row, m, "Imported")?;
        if changed {
            if ![
                "QueuedToClient",
                "Received",
                "Accepted",
                "PendingReview",
                "Approved",
                "Rejected",
                "Failed",
            ]
            .contains(&rules::text(&row, "status"))
            {
                row["status"] = json!("SubmitPackageImported");
            }
            tracking::bind(service, &mut row, &profiles::secret(service, &profile)?)?;
            row = tracking::save(tx, actor, row, Some(&profile))?;
            tracking::archive(tx, &row, bytes)?;
        }
        Ok((row, Vec::new(), 0))
    })?;
    Ok(
        json!({"success":true,"packagePath":"","workingDirectory":"","workingDirectoryKept":false,"manifest":package.manifest,
        "parsedReceipts":parsed,"trackingBatchId":row["id"],"trackingStatus":row["status"],"persistedReceiptCount":count,"storagePolicy":packages::POLICY,"message":if submit{"提交包已验证并归档，可送入当前官方客户端。"}else{"回执已验证并归档。"}}),
    )
}
