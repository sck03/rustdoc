//! Invoice interchange uses the normal save boundary for every mutation.
mod package;
use super::{
    NativeService, auth, catalog,
    error::{Result, conflict, error, invalid},
    media, records, report_assets,
    store::{self, Actor, Connection},
};
use crate::{contracts, generated_api::*, paths};
use package::Package;
use serde_json::{Value, json};
use std::{collections::BTreeMap, path::Path};

pub const OPERATIONS: &[Operation] = &[
    DOWNLOAD_INVOICE_TRANSFER_PACKAGE,
    SAVE_INVOICE_TRANSFER_PACKAGE_TO_PATH,
    PREVIEW_INVOICE_TRANSFER_PACKAGE,
    PREVIEW_UPLOADED_INVOICE_TRANSFER_PACKAGE,
    IMPORT_INVOICE_TRANSFER_PACKAGE,
    IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
];
pub const LOCAL: &[Operation] = &[
    SAVE_INVOICE_TRANSFER_PACKAGE_TO_PATH,
    PREVIEW_INVOICE_TRANSFER_PACKAGE,
    IMPORT_INVOICE_TRANSFER_PACKAGE,
];
pub const UPLOADS: &[Operation] = &[
    PREVIEW_UPLOADED_INVOICE_TRANSFER_PACKAGE,
    IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE,
];
const POLICY: &str = "单据包保留业务内容，导入按当前账号的数据范围和权限保存。";
fn text(v: &Value, key: &str) -> String {
    records::text(v, key)
}
fn clean(schema: &str, value: Value) -> Value {
    let mut result = contracts::dto(contracts::schema(schema), value);
    if let Some(fields) = result.as_object_mut() {
        for key in [
            "ownerUserId",
            "companyScope",
            "departmentId",
            "createdAt",
            "updatedAt",
            "rowVersion",
            "versionNumber",
            "id",
            "invoiceId",
            "customerId",
            "exporterId",
        ] {
            fields.remove(key);
        }
    }
    if let Some(items) = result["items"].as_array_mut() {
        for item in items {
            *item = clean("ApiInvoiceItemDto", item.take());
        }
    }
    result
}
fn equivalent(existing: &Value, incoming: &Value) -> bool {
    let mut existing = clean("ApiInvoiceDetailDto", existing.clone());
    let mut incoming = clean("ApiInvoiceDetailDto", incoming.clone());
    for v in [&mut existing, &mut incoming] {
        if let Some(o) = v.as_object_mut() {
            o.remove("status");
            o.remove("letterOfCreditSourcePath");
        }
    }
    existing == incoming
}
fn find_invoice(tx: &Connection, actor: &Actor, invoice: &Value) -> Result<Option<Value>> {
    Ok(store::all(tx, "invoices")?.into_iter().find(|v| {
        v["companyScope"] == actor.company
            && v["invoiceNo"] == invoice["invoiceNo"]
            && v["type"] == invoice["type"]
    }))
}
fn find_party(tx: &Connection, actor: &Actor, kind: &str, value: &Value) -> Result<Option<Value>> {
    let keys: &[&str] = if kind == "customers" {
        &["customerNameEN", "taxId"]
    } else {
        &["exporterNameEN", "exporterNameCN", "creditCode"]
    };
    Ok(store::all(tx, kind)?.into_iter().find(|v| {
        v["companyScope"] == actor.company
            && keys.iter().any(|key| {
                !text(value, key).is_empty()
                    && store::normalize(&text(value, key)) == store::normalize(&text(v, key))
            })
    }))
}
fn preview(tx: &Connection, actor: &Actor, package: &Package) -> Result<Value> {
    let existing = find_invoice(tx, actor, &package.invoice)?;
    let visible = existing
        .as_ref()
        .filter(|v| auth::visible(actor, "document.invoices", "view", v));
    let party_exists = |kind: &str, value: &Option<Value>| -> Result<bool> {
        Ok(match value {
            Some(v) => find_party(tx, actor, kind, v)?
                .is_some_and(|p| auth::visible(actor, "document.reference-data", "view", &p)),
            None => false,
        })
    };
    Ok(
        json!({"invoiceNo":package.invoice["invoiceNo"],"type":package.invoice["type"],"itemCount":package.invoice["items"].as_array().map(Vec::len).unwrap_or(0),
        "invoiceExists":existing.is_some(),"existingInvoiceId":visible.map(|v|v["id"].clone()).unwrap_or(json!(0)),
        "invoiceMatches":visible.is_some_and(|v|equivalent(v,&package.invoice)),"customerExists":party_exists("customers",&package.customer)?,"exporterExists":party_exists("exporters",&package.exporter)?}),
    )
}
fn image(
    tx: &Connection,
    actor: &Actor,
    package: &Package,
    value: &mut Value,
    field: &str,
    prefix: &str,
) -> Result<()> {
    if text(value, field).is_empty() {
        return Ok(());
    }
    let bytes = package
        .assets
        .get(field)
        .ok_or_else(|| invalid("单据包包含未随包提供的图片，请补齐图片后重新导出。"))?;
    let record = report_assets::register(tx, actor, bytes, field, 5 * 1024 * 1024)?;
    value[field] = json!(format!("{prefix}{}", text(&record, "resourceId")));
    Ok(())
}
fn party(
    tx: &Connection,
    actor: &Actor,
    kind: &str,
    source: Option<&Value>,
    invoice: &Value,
    package: &Package,
    date: chrono::NaiveDate,
) -> Result<i64> {
    let resource = catalog::resource(kind).ok_or_else(|| invalid("往来单位类型无效。"))?;
    let mut value = source
        .cloned()
        .unwrap_or_else(|| contracts::initial(contracts::schema(resource.schema)));
    let names: &[&str] = if kind == "customers" {
        &["customerNameEN"]
    } else {
        &["exporterNameEN", "exporterNameCN"]
    };
    for name in names {
        if text(&value, name).is_empty() {
            value[*name] = invoice[*name].clone();
        }
    }
    if names.iter().all(|name| text(&value, name).is_empty()) {
        return Ok(0);
    }
    if let Some(existing) = find_party(tx, actor, kind, &value)? {
        if !auth::visible(actor, "document.reference-data", "view", &existing) {
            return Err(error(403, "同名往来单位不在当前账号的数据范围内。"));
        }
        return existing["id"]
            .as_i64()
            .ok_or_else(|| invalid("往来单位缺少编号。"));
    }
    value = contracts::overlay(
        contracts::initial(contracts::schema(resource.schema)),
        &clean(resource.schema, value),
    );
    if kind == "exporters" {
        for key in ["docSealPath", "customsSealPath"] {
            image(tx, actor, package, &mut value, key, "Files/Seals/")?;
        }
    }
    Ok(
        records::save_in_transaction(tx, actor, resource, 0, &value, date)?["id"]
            .as_i64()
            .ok_or_else(|| invalid("往来单位缺少编号。"))?,
    )
}
fn target_no(tx: &Connection, actor: &Actor, invoice: &Value, requested: &str) -> Result<String> {
    let seed = if requested.is_empty() {
        format!("{}_IMPORTED", text(invoice, "invoiceNo"))
    } else {
        requested.into()
    };
    let mut candidate = invoice.clone();
    for counter in 0..10_000 {
        let name = if counter == 0 {
            seed.clone()
        } else {
            format!("{seed}{counter}")
        };
        candidate["invoiceNo"] = json!(name);
        if find_invoice(tx, actor, &candidate)?.is_none() {
            return Ok(name);
        }
    }
    Err(conflict("无法分配新的发票号，请输入另一个编号。"))
}
fn import(
    service: &NativeService,
    actor: &Actor,
    package: &Package,
    body: &Value,
) -> Result<Value> {
    if !package.checksum_valid {
        return Err(invalid("单据包校验失败，导入已取消。"));
    }
    auth::authorize(actor, "document.invoices", "operate")?;
    let action = text(body, "conflictAction");
    let action = if action.is_empty() {
        "Skip"
    } else {
        action.as_str()
    };
    if !["Skip", "Overwrite", "NewInvoiceNo", "AppendItems"].contains(&action) {
        return Err(invalid("单据包冲突处理方式无效。"));
    }
    let date = service.clock.now().map_err(invalid)?.today;
    service.store.transaction(|tx| {
        let preview=preview(tx,actor,package)?;
        let existing=find_invoice(tx,actor,&package.invoice)?;
        let mut invoice=contracts::overlay(contracts::initial(contracts::schema("ApiInvoiceDetailDto")),&clean("ApiInvoiceDetailDto",package.invoice.clone()));
        for item in invoice["items"].as_array_mut().into_iter().flatten() {
            *item=contracts::overlay(contracts::initial(contracts::schema("ApiInvoiceItemDto")),&item.take());
        }
        let result=|id:Value,number:Value,message:&str|json!({"success":true,"result":{"success":true,"message":message,"invoiceId":id,"finalInvoiceNo":number,"actionTaken":action},"preview":preview,"storagePolicy":POLICY,"message":message});
        if action=="Skip"&&existing.is_some(){return Ok(result(if preview["existingInvoiceId"]==0{Value::Null}else{preview["existingInvoiceId"].clone()},invoice["invoiceNo"].clone(),"已跳过重复单据。"));}
        let mut id=0;
        if let Some(previous)=&existing {
            if action=="NewInvoiceNo" {invoice["invoiceNo"]=json!(target_no(tx,actor,&invoice,&text(body,"newInvoiceNo"))?);}
            else {
                if !auth::visible(actor,"document.invoices","operate",previous){return Err(error(403,"当前账号无权覆盖或追加目标单据，请使用新发票号。"));}
                if previous["status"]!="Draft"{return Err(conflict("目标单据已锁定，请先撤销核对回到草稿。"));}
                id=previous["id"].as_i64().ok_or_else(||invalid("单据缺少编号。"))?;
                if action=="AppendItems" {
                    let imported=invoice["items"].clone();
                    invoice=previous.clone();
                    let rows=invoice["items"].as_array_mut().ok_or_else(||invalid("目标单据明细无效。"))?;
                    rows.extend(imported.as_array().ok_or_else(||invalid("导入明细无效。"))?.iter().cloned());
                }
                invoice["expectedVersion"]=previous["versionNumber"].clone();
            }
        }
        if action!="AppendItems"||id==0 {
            invoice["customerId"]=json!(party(tx,actor,"customers",package.customer.as_ref(),&invoice,package,date)?);
            invoice["exporterId"]=json!(party(tx,actor,"exporters",package.exporter.as_ref(),&invoice,package,date)?);
            image(tx,actor,package,&mut invoice,"shippingMarksImage","Files/ShippingMarks/")?;
            invoice["letterOfCreditSourcePath"]=json!("");
        }
        invoice["id"]=json!(id);invoice["status"]=json!("Draft");
        let saved=records::save_in_transaction(tx,actor,catalog::resource("invoices").unwrap(),id,&invoice,date)?;
        Ok(result(saved["id"].clone(),saved["invoiceNo"].clone(),"单据导入成功。"))
    })
}
fn collect_image(
    tx: &Connection,
    actor: &Actor,
    record: &Value,
    field: &str,
    prefix: &str,
    assets: &mut BTreeMap<String, Vec<u8>>,
) -> Result<()> {
    let reference = text(record, field);
    if reference.is_empty() {
        return Ok(());
    }
    let id = reference
        .strip_prefix(prefix)
        .ok_or_else(|| invalid("单据图片不是受管资源。"))?;
    assets.insert(
        field.into(),
        report_assets::read(tx, actor, id, None)?.bytes,
    );
    Ok(())
}
fn export(service: &NativeService, actor: &Actor, id: i64) -> Result<Vec<u8>> {
    let package = service.store.transaction(|tx| {
        let source = store::get(tx, "invoices", id)?;
        if !auth::visible(actor, "document.invoices", "view", &source)
            || !auth::visible(actor, "document.invoice-output", "export-zip", &source)
        {
            return Err(error(403, "单据不在当前账号的导出范围内。"));
        }
        let party = |kind: &str, key: &str| -> Result<Option<Value>> {
            if let Some(id) = source[key].as_i64().filter(|id| *id > 0) {
                let value = store::get(tx, kind, id)?;
                if !auth::visible(actor, "document.reference-data", "view", &value) {
                    return Err(error(403, "关联往来单位不在当前账号的数据范围内。"));
                }
                Ok(Some(value))
            } else {
                Ok(None)
            }
        };
        let customer = party("customers", "customerId")?;
        let exporter = party("exporters", "exporterId")?;
        let mut assets = BTreeMap::new();
        collect_image(
            tx,
            actor,
            &source,
            "shippingMarksImage",
            "Files/ShippingMarks/",
            &mut assets,
        )?;
        if let Some(exporter) = &exporter {
            for field in ["docSealPath", "customsSealPath"] {
                collect_image(tx, actor, exporter, field, "Files/Seals/", &mut assets)?;
            }
        }
        let mut invoice = clean("ApiInvoiceDetailDto", source);
        invoice["letterOfCreditSourcePath"] = json!("");
        Ok(Package {
            invoice,
            customer: customer.map(|v| clean("ApiCustomerDto", v)),
            exporter: exporter.map(|v| clean("ApiExporterDto", v)),
            assets,
            checksum_valid: true,
        })
    })?;
    package::write(&package)
}
pub fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
    name: &str,
    bytes: &[u8],
) -> Result<Value> {
    if !paths::valid_file_name(name)
        || !Path::new(name)
            .extension()
            .is_some_and(|ext| ext.eq_ignore_ascii_case("edpkg"))
    {
        return Err(invalid("请选择有效的 .edpkg 单据包。"));
    }
    let package = package::read(bytes)?;
    if operation == IMPORT_UPLOADED_INVOICE_TRANSFER_PACKAGE
        || operation == IMPORT_INVOICE_TRANSFER_PACKAGE
    {
        return import(service, actor, &package, body);
    }
    let preview = preview(&*service.store.connection()?, actor, &package)?;
    Ok(
        json!({"checksumValid":package.checksum_valid,"checksumMessage":if package.checksum_valid{"校验通过"}else{"校验失败，不能导入"},"preview":preview,"storagePolicy":POLICY}),
    )
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Vec<u8>> {
    if LOCAL.contains(&operation) && service.provider()? != "SQLite" {
        return Err(error(403, "服务器不支持本机文件路径。"));
    }
    if [
        DOWNLOAD_INVOICE_TRANSFER_PACKAGE,
        SAVE_INVOICE_TRANSFER_PACKAGE_TO_PATH,
    ]
    .contains(&operation)
    {
        let id = records::id(parameters)?;
        let bytes = export(service, actor, id)?;
        if operation == DOWNLOAD_INVOICE_TRANSFER_PACKAGE {
            return Ok(bytes);
        }
        let path = Path::new(body["packagePath"].as_str().unwrap_or(""));
        paths::ensure_safe_absolute(path).map_err(invalid)?;
        if !path
            .file_name()
            .and_then(|s| s.to_str())
            .is_some_and(paths::valid_file_name)
            || !path
                .extension()
                .is_some_and(|e| e.eq_ignore_ascii_case("edpkg"))
        {
            return Err(invalid("请选择有效的 .edpkg 输出文件。"));
        }
        paths::atomic_write(path, &bytes).map_err(super::error::unavailable)?;
        return serde_json::to_vec(&json!({"success":true,"invoiceId":id,"packagePath":path,"storagePolicy":POLICY,"message":"单据包已保存。"})).map_err(Into::into);
    }
    if !LOCAL.contains(&operation) {
        return Err(invalid("请通过文件上传传入单据包。"));
    }
    let path = Path::new(body["packagePath"].as_str().unwrap_or(""));
    let name = path
        .file_name()
        .and_then(|s| s.to_str())
        .ok_or_else(|| invalid("单据包路径无效。"))?;
    let bytes = media::read_local(path, package::MAX_INPUT)?;
    serde_json::to_vec(&upload(service, actor, operation, body, name, &bytes)?).map_err(Into::into)
}
