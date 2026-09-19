use super::{
    auth, catalog,
    error::{Result, conflict, invalid},
    records::{required, text},
    store::{self, Actor, Store},
};
use crate::{generated_api::*, invoice::InvoiceDraft};
use serde_json::{Value, json};

pub const ACTIONS: &[Operation] = &[
    TRANSITION_INVOICE_STATUS,
    UNVERIFY_INVOICE,
    CLONE_INVOICE,
    CLONE_INVOICE_AS_TYPE,
    DEACTIVATE_CRM_CUSTOMER,
    RESTORE_CRM_CUSTOMER,
    ADMIT_SUPPLIER,
    DEACTIVATE_SUPPLIER,
    RESTORE_SUPPLIER,
    PUBLISH_USER_REPORT_TEMPLATE,
    SHARE_USER_REPORT_TEMPLATE,
    DISABLE_USER_REPORT_TEMPLATE,
    RESTORE_USER_REPORT_TEMPLATE,
    ARCHIVE_USER_REPORT_TEMPLATE,
    CLONE_USER_REPORT_TEMPLATE,
];

pub fn review(body: &Value) -> Result<Value> {
    let mut issues = vec![];
    for (field, label) in [
        ("invoiceNo", "发票号"),
        ("invoiceDate", "发票日期"),
        ("exporterNameEN", "出口商"),
        ("customerNameEN", "客户"),
        ("currency", "币种"),
        ("destinationCountry", "目的国"),
        ("portOfLoading", "装货港"),
        ("portOfDestination", "目的港"),
    ] {
        if text(body, field).is_empty() {
            issues.push(json!({"code":"required","field":field,"fieldPath":field,"message":format!("请填写{label}"),"severity":"Error","rowNumber":null}));
        }
    }
    if let Some(items) = body["items"].as_array() {
        for (index, item) in items.iter().enumerate() {
            for (field, label) in [
                ("styleNo", "款号"),
                ("styleName", "英文品名"),
                ("hsCode", "HS 编码"),
            ] {
                if text(item, field).is_empty() {
                    issues.push(json!({"code":"required","field":field,"fieldPath":format!("items[{index}].{field}"),"message":format!("第 {} 行缺少{label}",index+1),"severity":"Error","rowNumber":index+1}));
                }
            }
        }
    } else {
        issues.push(json!({"code":"items","message":"请录入商品明细","severity":"Error"}));
    }
    Ok(json!({"ready":issues.is_empty(),"issues":issues}))
}

pub fn action(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    id: i64,
    body: Value,
) -> Result<Value> {
    let (kind, action) = match operation.id {
        "TransitionInvoiceStatus" => ("invoices", "transition"),
        "UnverifyInvoice" => ("invoices", "unverify"),
        "CloneInvoice" | "CloneInvoiceAsType" => ("invoices", "create"),
        "DeactivateCrmCustomer" => ("crm-customers", "deactivate"),
        "RestoreCrmCustomer" => ("crm-customers", "restore"),
        "AdmitSupplier" => ("suppliers", "admit"),
        "DeactivateSupplier" => ("suppliers", "deactivate"),
        "RestoreSupplier" => ("suppliers", "restore"),
        "PublishUserReportTemplate" => ("report-templates", "publish"),
        "ShareUserReportTemplate" => ("report-templates", "share"),
        "DisableUserReportTemplate" => ("report-templates", "deactivate"),
        "RestoreUserReportTemplate" => ("report-templates", "restore"),
        "ArchiveUserReportTemplate" => ("report-templates", "archive"),
        "CloneUserReportTemplate" => ("report-templates", "clone"),
        _ => return Err(invalid("业务动作未注册。")),
    };
    let resource = catalog::resource(kind).ok_or_else(|| invalid("业务资源未注册。"))?;
    let permission_action = auth::operation_action(operation, resource.permission, action)?;
    auth::authorize(actor, resource.permission, permission_action)?;
    let mut saved=store.transaction(|transaction|{
        let mut value=store::get(transaction,kind,id)?;
        if !auth::visible(actor,resource.permission,permission_action,&value){return Err(super::error::error(403,"没有办理此记录的权限。"));}
        let cloning=matches!(operation.id,"CloneInvoice"|"CloneInvoiceAsType"|"CloneUserReportTemplate");
        if !cloning{store::check_version(&value,store::expected(&body))?;}
        let state=text(&value,"status");
        match operation.id {
            "TransitionInvoiceStatus"=>{
                let next=text(&body,"targetStatus");let allowed=matches!((state.as_str(),next.as_str()),("Draft","Verified")|("Verified","Shipped")|("Shipped","Completed"))||(state!="Cancelled"&&next=="Cancelled");
                if !allowed{return Err(conflict("发票状态不允许此项变更。"));}
                if next=="Verified"{let review=review(&value)?;if review["ready"]!=true{return Err(invalid(format!("发票核对未通过：{}",review["issues"][0]["message"].as_str().unwrap_or("资料未齐全"))));}}
                if next=="Cancelled"{required(&body,"note","作废原因",500)?;}
                value["status"]=json!(next);
            },
            "UnverifyInvoice"=>{if !["Verified","Shipped","Completed"].contains(&state.as_str()){return Err(conflict("当前发票状态不能撤销核对。"));}required(&body,"note","撤销核对原因",500)?;value["status"]=json!("Draft");},
            "CloneInvoice"|"CloneInvoiceAsType"=>{
                value["id"]=json!(0);value["status"]=json!("Draft");value["rowVersion"]=json!("");value["invoiceNo"]=json!(format!("{}-COPY-{}",text(&value,"invoiceNo"),&crate::paths::nonce().map_err(super::error::unavailable)?[..6]));
                if operation==CLONE_INVOICE_AS_TYPE{value["type"]=body["type"].clone();}
            },
            "DeactivateCrmCustomer"=>{if ["暂停","已流失"].contains(&state.as_str()){return Err(conflict("客户已处于暂停或流失状态。"));}value["status"]=json!("暂停");},"RestoreCrmCustomer"=>{if !["暂停","已流失"].contains(&state.as_str()){return Err(conflict("只有暂停或流失客户可以恢复。"));}value["status"]=json!("跟进中");},
            "AdmitSupplier"=>{if state!="考察中"{return Err(conflict("仅能准入考察中的供应商。"));}value["status"]=json!("合作中");},"DeactivateSupplier"=>{if state=="停用"{return Err(conflict("供应商已经停用。"));}value["status"]=json!("停用");},"RestoreSupplier"=>{if !["暂停","停用"].contains(&state.as_str()){return Err(conflict("只有暂停或停用的供应商可以恢复考察。"));}value["status"]=json!("考察中");},
            _=>{
                let next=match action{"publish"=>"Published","share"=>"Shared","deactivate"=>"Disabled","restore"=>"Draft","archive"=>"Archived","clone"=>"Draft",_=>return Err(invalid("模板操作无效。"))};
                if matches!(action,"publish"|"share")&&text(&value,if kind=="report-templates"{"contentHtml"}else{"bodyHtml"}).is_empty(){return Err(invalid("模板内容不能为空。"));}
                value["status"]=json!(next);if cloning{value["id"]=json!(0);value["name"]=json!(format!("{} 副本",text(&value,"name")));}
            },
        }
        let identity=if resource.identity.is_empty(){None}else{Some(text(&value,resource.identity))};
        let saved=store::save(transaction,kind,if cloning{0}else{id},value,identity,actor,action)?;
        if ["report-templates","email-templates"].contains(&kind){store::save(transaction,"template-versions",0,json!({"templateKind":kind,"templateId":saved["id"],"content":saved,"note":text(&body,"note")}),None,actor,"version")?;}
        Ok(saved)
    })?;
    if kind == "invoices" {
        let dto: ApiInvoiceDetailDto = serde_json::from_value(saved)?;
        let invoice = InvoiceDraft::from_dto(dto).build().map_err(invalid)?;
        saved = serde_json::to_value(invoice)?;
        return Ok(json!({"success":true,"message":"操作完成","invoice":saved}));
    }
    Ok(saved)
}
