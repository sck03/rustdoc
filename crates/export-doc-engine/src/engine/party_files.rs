//! Customer and supplier files use the same parsing, validation and single-use preview flow.
use super::{
    NativeService, auth, catalog,
    error::{Result, invalid, unavailable},
    import_previews,
    records::text,
    store::{self, Actor},
};
use crate::{contracts, generated_api::*, operation, paths::valid_file_name};
use export_doc_domain::party;
use serde_json::{Value, json};
use std::collections::BTreeSet;

pub const OPERATIONS: &[Operation] = &[
    PREVIEW_CRM_CUSTOMER_IMPORT,
    IMPORT_CRM_CUSTOMERS,
    EXPORT_CRM_CUSTOMERS,
    PREVIEW_SUPPLIER_IMPORT,
    IMPORT_SUPPLIERS,
    EXPORT_SUPPLIERS,
];
pub const UPLOADS: &[Operation] = &[PREVIEW_CRM_CUSTOMER_IMPORT, PREVIEW_SUPPLIER_IMPORT];
pub const EXPORTS: &[Operation] = &[EXPORT_CRM_CUSTOMERS, EXPORT_SUPPLIERS];
fn check() -> std::result::Result<(), String> {
    operation::check().map_err(|cause| cause.to_string())
}
fn supplier(operation: Operation) -> bool {
    [PREVIEW_SUPPLIER_IMPORT, IMPORT_SUPPLIERS, EXPORT_SUPPLIERS].contains(&operation)
}
fn kind(supplier: bool) -> &'static str {
    if supplier {
        "suppliers"
    } else {
        "crm-customers"
    }
}
fn contact_kind(supplier: bool) -> &'static str {
    if supplier {
        "supplier-contacts"
    } else {
        "crm-contacts"
    }
}
fn relation(supplier: bool) -> &'static str {
    if supplier {
        "supplierCompanyId"
    } else {
        "crmCustomerId"
    }
}
pub fn upload(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    name: &str,
    bytes: &[u8],
) -> Result<Value> {
    if !valid_file_name(name) {
        return Err(invalid("导入文件名无效。"));
    }
    let supplier = supplier(operation);
    let table = export_doc_excel::read_table(bytes, name, 5000, &check).map_err(invalid)?;
    let mut rows = party::map_rows(&table, supplier).map_err(invalid)?;
    let resource = catalog::resource(kind(supplier)).unwrap();
    service.store.transaction(|tx| {
        let mut names: BTreeSet<_> = store::all(tx,resource.key)?.iter().filter(|row| auth::visible(actor,resource.permission,"view",row))
            .map(|row| store::normalize(&text(row,"name"))).collect();
        for row in &mut rows {
            operation::check()?;
            let key = store::normalize(&text(row,"name"));
            row["isDuplicate"] = json!(!key.is_empty() && !names.insert(key));
        }
        let identity = import_previews::save(tx,actor,&service.clock,resource.key,&rows)?;
        Ok(json!({"totalRows":rows.len(),"validRows":rows.iter().filter(|row| row["error"] == "" && row["isDuplicate"] != true).count(),
            "duplicateRows":rows.iter().filter(|row| row["isDuplicate"] == true).count(),"rows":rows,"previewId":identity}))
    })
}
fn import(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    body: &Value,
) -> Result<Value> {
    let supplier = supplier(operation);
    let resource = catalog::resource(kind(supplier)).unwrap();
    let contacts = catalog::resource(contact_kind(supplier)).unwrap();
    service.store.transaction(|tx| {
        let rows = import_previews::consume(tx,actor,&service.clock,resource.key,&text(body,"previewId"))?;
        let mut names: BTreeSet<_> = store::all(tx,resource.key)?.iter().filter(|row| auth::visible(actor,resource.permission,"view",row))
            .map(|row| store::normalize(&text(row,"name"))).collect();
        let (mut created,mut contact_count,mut skipped) = (0,0,0);
        for row in rows {
            operation::check()?;
            let row = party::normalize(&row,supplier);
            let key = store::normalize(&text(&row,"name"));
            if row["error"] != "" || key.is_empty() || !names.insert(key) { skipped += 1; continue; }
            let mut party = contracts::initial(contracts::schema(resource.schema));
            for key in ["name","countryRegion","website","status","notes","source","category","mainProducts"] {
                if let Some(value) = row.get(key) { party[key] = value.clone(); }
            }
            let party = store::save(tx,resource.key,0,party,None,actor,"create")?;
            created += 1;
            if row["contactName"] != "" {
                let mut contact = contracts::initial(contracts::schema(contacts.schema));
                contact[relation(supplier)] = party["id"].clone();
                for (from,to) in [("contactName","name"),("contactTitle","title"),("contactEmail","email"),("contactPhone","phone")] { contact[to] = row[from].clone(); }
                contact["isPrimary"] = json!(true);
                store::save(tx,contacts.key,0,contact,None,actor,"create")?;
                contact_count += 1;
            }
        }
        Ok(if supplier { json!({"createdSuppliers":created,"createdContacts":contact_count,"skippedRows":skipped}) }
            else { json!({"createdCustomers":created,"createdContacts":contact_count,"skippedDuplicates":skipped}) })
    })
}
fn export(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
) -> Result<Vec<u8>> {
    let supplier = supplier(operation);
    let resource = catalog::resource(kind(supplier)).unwrap();
    let read = |key: &str| {
        query
            .iter()
            .find(|(name, _)| *name == key)
            .map(|(_, value)| value.as_str())
            .unwrap_or("")
    };
    let keyword = store::normalize(read("keyword"));
    let rows = service.store.transaction(|tx| {
        let mut rows: Vec<_> = store::all(tx, resource.key)?
            .into_iter()
            .filter(|row| {
                auth::visible(actor, resource.permission, "export", row)
                    && (read("status").is_empty() || row["status"] == read("status"))
                    && (keyword.is_empty()
                        || [
                            "name",
                            "countryRegion",
                            "website",
                            "source",
                            "notes",
                            "category",
                            "mainProducts",
                        ]
                        .iter()
                        .any(|key| store::normalize(&text(row, key)).contains(&keyword)))
            })
            .collect();
        if rows.len() > 10_000 {
            return Err(invalid("当前筛选超过 10000 条，请缩小范围后导出。"));
        }
        rows.sort_by_key(|row| text(row, "name"));
        let mut contacts = store::all(tx, contact_kind(supplier))?;
        contacts.sort_by_key(|row| (row["isPrimary"] != true, row["id"].as_i64()));
        for row in &mut rows {
            operation::check()?;
            let contact = contacts
                .iter()
                .find(|contact| contact[relation(supplier)] == row["id"]);
            for (from, to) in [
                ("name", "contactName"),
                ("title", "contactTitle"),
                ("email", "contactEmail"),
                ("phone", "contactPhone"),
                ("instantMessaging", "instantMessaging"),
            ] {
                row[to] = contact
                    .map(|row| json!(text(row, from)))
                    .unwrap_or(json!(""));
            }
        }
        Ok(rows)
    })?;
    let mut columns = if supplier {
        vec![
            ("name", "供应商名称"),
            ("countryRegion", "国家/地区"),
            ("category", "分类"),
            ("website", "网站"),
            ("status", "状态"),
            ("mainProducts", "主要产品"),
            ("notes", "备注"),
        ]
    } else {
        vec![
            ("name", "客户名称"),
            ("countryRegion", "国家/地区"),
            ("website", "网站"),
            ("status", "状态"),
            ("source", "来源"),
            ("notes", "备注"),
        ]
    };
    columns.extend([
        (
            "contactName",
            if supplier {
                "联系人"
            } else {
                "主要联系人"
            },
        ),
        ("contactTitle", "职位"),
        ("contactEmail", "邮箱"),
        ("contactPhone", "电话"),
    ]);
    if !supplier {
        columns.push(("instantMessaging", "即时通讯"));
    }
    export_doc_excel::table(&columns, &rows, &check).map_err(unavailable)
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
    body: &Value,
) -> Result<Vec<u8>> {
    if EXPORTS.contains(&operation) {
        export(service, actor, operation, query)
    } else if UPLOADS.contains(&operation) {
        Err(invalid("请选择要上传的导入文件。"))
    } else {
        serde_json::to_vec(&import(service, actor, operation, body)?).map_err(Into::into)
    }
}
