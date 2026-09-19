use super::*;
pub const OPERATIONS: &[Operation] = &[
    GET_SINGLE_WINDOW_REFERENCE_CATALOG,
    UPDATE_SINGLE_WINDOW_REFERENCE_CATALOG,
    RESET_SINGLE_WINDOW_REFERENCE_CATALOG,
    IMPORT_SINGLE_WINDOW_REFERENCE_CATALOG_JSON,
    GET_CUSTOMS_COO_ISSUING_AUTHORITIES,
    GET_CUSTOMS_COO_EDITOR_OPTIONS,
    LIST_CUSTOMS_COO_PRODUCER_PROFILES,
    GET_CUSTOMS_COO_PRODUCER_PROFILE,
    CREATE_CUSTOMS_COO_PRODUCER_PROFILE,
    UPDATE_CUSTOMS_COO_PRODUCER_PROFILE,
    DELETE_CUSTOMS_COO_PRODUCER_PROFILE,
];
const PRODUCERS: &str = "sw-producers";
const POLICY: &str = "资料保存在当前业务数据库，按账号数据范围访问。";
pub fn catalog(tx: &Connection) -> Result<Value> {
    match tx.settings("sw-reference-catalog")? {
        Some(value) => {
            rules::catalog::validate(&value).map_err(unavailable)?;
            Ok(value)
        }
        None => Ok(rules::catalog::defaults().clone()),
    }
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let result=service.store.transaction(|tx|match operation{
        GET_CUSTOMS_COO_ISSUING_AUTHORITIES=>Ok(rules::catalog::authorities()),
        GET_CUSTOMS_COO_EDITOR_OPTIONS=>Ok(rules::catalog::editor_options()),
        GET_SINGLE_WINDOW_REFERENCE_CATALOG=>Ok(json!({"catalog":catalog(tx)?,"storagePolicy":POLICY})),
        UPDATE_SINGLE_WINDOW_REFERENCE_CATALOG|RESET_SINGLE_WINDOW_REFERENCE_CATALOG|IMPORT_SINGLE_WINDOW_REFERENCE_CATALOG_JSON=>{
            let value=if operation==RESET_SINGLE_WINDOW_REFERENCE_CATALOG{rules::catalog::defaults().clone()}else if operation==IMPORT_SINGLE_WINDOW_REFERENCE_CATALOG_JSON{body.clone()}else{body["catalog"].clone()};
            rules::catalog::validate(&value).map_err(invalid)?;
            tx.set_settings("sw-reference-catalog",1,&value)?;
            tx.append_audit_details(&export_doc_storage::AuditWrite{kind:"sw-reference-catalog",record_id:0,version:1,action:"edit",actor_id:actor.id,occurred_at:&store::timestamp(),note:""},&json!({"groups":6}))?;
            Ok(json!({"success":true,"catalog":value,"message":"申报词典已保存。","storagePolicy":POLICY}))
        }
        LIST_CUSTOMS_COO_PRODUCER_PROFILES=>{
            let keyword=store::normalize(parameter(query,"keyword"));let mut rows=store::all(tx,PRODUCERS)?;
            rows.retain(|r|auth::visible(actor,PERMISSION,"view",r)&&["ciqRegNo","prdcEtpsName","lastSourceStyleNo","lastInvoiceNo"].iter().any(|k|store::normalize(rules::text(r,k)).contains(&keyword)));
            rows.sort_by(|a,b|b["updatedAt"].as_str().cmp(&a["updatedAt"].as_str()));
            Ok(json!({"totalCount":rows.len(),"items":rows,"storagePolicy":POLICY}))
        }
        GET_CUSTOMS_COO_PRODUCER_PROFILE=>{
            let row=store::get(tx,PRODUCERS,records::id(parameters)?)?;
            if !auth::visible(actor,PERMISSION,"view",&row){return Err(error(403,"无权查看该生产企业。"));}
            Ok(json!({"profile":row,"storagePolicy":POLICY}))
        }
        CREATE_CUSTOMS_COO_PRODUCER_PROFILE|UPDATE_CUSTOMS_COO_PRODUCER_PROFILE=>{
            let id=if operation==UPDATE_CUSTOMS_COO_PRODUCER_PROFILE{records::id(parameters)?}else{0};
            let value=producer(tx,actor,id,&body["profile"],false)?;
            Ok(json!({"success":true,"id":value["id"],"profile":value,"message":"生产企业资料已保存。","storagePolicy":POLICY}))
        }
        DELETE_CUSTOMS_COO_PRODUCER_PROFILE=>{
            let id=records::id(parameters)?;let previous=store::get(tx,PRODUCERS,id)?;
            if !auth::visible(actor,PERMISSION,"delete",&previous){return Err(error(403,"无权删除该生产企业。"));}
            if !tx.delete(PRODUCERS,id,previous["versionNumber"].as_i64().ok_or_else(||unavailable("生产企业版本无效。"))?)?{return Err(super::super::error::conflict("生产企业资料已变化。"));}
            tx.append_audit_details(&export_doc_storage::AuditWrite{kind:PRODUCERS,record_id:id,version:previous["versionNumber"].as_i64().unwrap_or(1),action:"delete",actor_id:actor.id,occurred_at:&store::timestamp(),note:""},&json!({"name":previous["prdcEtpsName"]}))?;
            Ok(json!({"success":true,"message":"生产企业资料已删除。"}))
        }
        _=>Err(invalid("资料操作无效。")),
    })?;
    Ok(contracts::dto(contracts::response(operation.id), result))
}
fn producer(
    tx: &Connection,
    actor: &Actor,
    id: i64,
    input: &Value,
    remember: bool,
) -> Result<Value> {
    export_doc_contracts::validation::structure(
        contracts::schema("ApiCustomsCooProducerProfileInputDto"),
        input,
    )
    .map_err(invalid)?;
    let mut input = contracts::dto(
        contracts::schema("ApiCustomsCooProducerProfileDto"),
        input.clone(),
    );
    if rules::text(&input, "ciqRegNo").is_empty() || rules::text(&input, "prdcEtpsName").is_empty()
    {
        return Err(invalid("生产企业组织机构代码和名称不能为空。"));
    }
    for (key, max) in [
        ("ciqRegNo", 10),
        ("prdcEtpsName", 400),
        ("prdcEtpsConcEr", 20),
        ("prdcEtpsTel", 20),
        ("producer", 1000),
        ("producerTel", 50),
        ("producerFax", 50),
        ("producerEmail", 50),
        ("producerSertFlag", 1),
    ] {
        if rules::text(&input, key).encode_utf16().count() > max {
            return Err(invalid(format!(
                "{}长度不能超过 {max}。",
                rules::label("goods", key)
            )));
        }
    }
    let company = if id > 0 {
        let previous = store::get(tx, PRODUCERS, id)?;
        if !auth::visible(actor, PERMISSION, "edit", &previous) {
            return Err(error(403, "无权修改该生产企业。"));
        }
        input["expectedVersion"] = previous["versionNumber"].clone();
        input["lastUsedAt"] = previous["lastUsedAt"].clone();
        rules::text(&previous, "companyScope").to_owned()
    } else {
        actor.company.clone()
    };
    if remember {
        input["lastUsedAt"] = json!(store::timestamp());
    }
    let identity = serde_json::to_string(&[
        company.as_str(),
        rules::text(&input, "ciqRegNo"),
        rules::text(&input, "prdcEtpsName"),
    ])?;
    store::save(
        tx,
        PRODUCERS,
        id,
        input,
        Some(identity),
        actor,
        if id == 0 { "create" } else { "edit" },
    )
}
pub fn remember(tx: &Connection, actor: &Actor, document: &Value) -> Result<()> {
    let mut settings = tx
        .settings("settings")?
        .unwrap_or(settings::defaults(tx.provider())?);
    let mut changed = false;
    for key in [
        "applName",
        "applicant",
        "applTel",
        "orgCode",
        "fetchPlace",
        "aplAdd",
    ] {
        let text = rules::text(document, key);
        if !text.is_empty() && settings["singleWindow"]["customsCooDefaults"][key] != text {
            settings["singleWindow"]["customsCooDefaults"][key] = json!(text);
            changed = true;
        }
    }
    if changed {
        let version = settings["revision"].as_i64().unwrap_or(0) + 1;
        settings["revision"] = json!(version);
        tx.set_settings("settings", version, &settings)?;
    }
    for row in document["items"].as_array().into_iter().flatten() {
        if rules::text(row, "ciqRegNo").is_empty() || rules::text(row, "prdcEtpsName").is_empty() {
            continue;
        }
        let identity = store::normalize(&serde_json::to_string(&[
            actor.company.as_str(),
            rules::text(row, "ciqRegNo"),
            rules::text(row, "prdcEtpsName"),
        ])?);
        let existing = tx.find_identity(PRODUCERS, &identity)?;
        let id = existing
            .as_ref()
            .and_then(|r| r["id"].as_i64())
            .unwrap_or(0);
        let mut input = contracts::dto(
            contracts::schema("ApiCustomsCooProducerProfileInputDto"),
            row.clone(),
        );
        input["lastInvoiceNo"] = document["invoiceNo"].clone();
        input["lastContractNo"] = document["contractNo"].clone();
        input["lastSourceStyleNo"] = row["sourceStyleNo"].clone();
        producer(tx, actor, id, &input, true)?;
    }
    Ok(())
}
