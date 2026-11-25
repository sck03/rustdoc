use super::{
    auth, catalog,
    error::{Result, conflict, invalid},
    records::{required, text},
    store::{self, Actor, Store},
};
use crate::{generated_api::*, invoice::InvoiceDraft};
use serde_json::{Value, json};
mod invoice_clone;

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
];

pub fn review(body: &Value) -> Result<Value> {
    let invoice = serde_json::from_value(body.clone())
        .map_err(|cause| invalid(format!("发票字段无效：{cause}")))?;
    Ok(serde_json::to_value(
        export_doc_domain::invoice_review::review(&invoice),
    )?)
}

pub fn profit(body: &Value) -> Result<Value> {
    use export_doc_domain::number::format_decimal;
    use rust_decimal::Decimal;
    let dto: ApiInvoiceDetailDto = serde_json::from_value(body["invoice"].clone())
        .map_err(|cause| invalid(cause.to_string()))?;
    let invoice = InvoiceDraft::from_dto(dto).build().map_err(invalid)?;
    let result = export_doc_domain::invoice_profit::calculate(&invoice).map_err(invalid)?;
    let rate = result.exchange_rate;
    let sales_rmb = result.sales_rmb;
    let profit = result.gross_profit;
    let margin = result.margin;
    let percent = margin
        .checked_mul(Decimal::from(100))
        .ok_or_else(|| invalid("利润率显示超出范围。"))?;
    Ok(
        json!({"currency":invoice.currency,"salesTotal":invoice.total_amount,"exchangeRate":rate,"salesRmb":sales_rmb,"purchaseCost":invoice.total_purchase_amount,"taxRefund":invoice.total_tax_refund_amount,"grossProfit":profit,"margin":margin,"salesTotalText":format!("{} {}",invoice.currency,format_decimal(invoice.total_amount,2)),"exchangeRateText":rate.map(|rate|format_decimal(rate,4)).unwrap_or("未设置".into()),"salesRmbText":format!("¥ {}",format_decimal(sales_rmb,2)),"purchaseCostText":format!("- ¥ {}",format_decimal(invoice.total_purchase_amount,2)),"taxRefundText":format!("+ ¥ {}",format_decimal(invoice.total_tax_refund_amount,2)),"grossProfitText":format!("¥ {}",format_decimal(profit,2)),"marginText":format!("{}%",format_decimal(percent,2)),"storagePolicy":"本机数据"}),
    )
}

pub fn action(
    store: &Store,
    actor: &Actor,
    operation: Operation,
    id: i64,
    body: Value,
    business_date: chrono::NaiveDate,
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
        _ => return Err(invalid("业务动作未注册。")),
    };
    let resource = catalog::resource(kind).ok_or_else(|| invalid("业务资源未注册。"))?;
    let permission_action = auth::operation_action(operation, resource.permission)?;
    auth::authorize(actor, resource.permission, permission_action)?;
    store.transaction_as(actor, |transaction, actor|{
        let mut value=store::get(transaction,kind,id)?;
        if !auth::visible(actor,resource.permission,permission_action,&value){return Err(super::error::error(403,"没有办理此记录的权限。"));}
        if matches!(operation.id,"CloneInvoice"|"CloneInvoiceAsType") {
            return invoice_clone::save(transaction, actor, resource, operation, &value, body, business_date);
        }
        store::check_version(&value,store::expected(&body))?;
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
            "DeactivateCrmCustomer"=>{if ["暂停","已流失"].contains(&state.as_str()){return Err(conflict("客户已处于暂停或流失状态。"));}value["status"]=json!("暂停");},"RestoreCrmCustomer"=>{if !["暂停","已流失"].contains(&state.as_str()){return Err(conflict("只有暂停或流失客户可以恢复。"));}value["status"]=json!("跟进中");},
            "AdmitSupplier"=>{if state!="考察中"{return Err(conflict("仅能准入考察中的供应商。"));}value["status"]=json!("合作中");},"DeactivateSupplier"=>{if state=="停用"{return Err(conflict("供应商已经停用。"));}value["status"]=json!("停用");},"RestoreSupplier"=>{if !["暂停","停用"].contains(&state.as_str()){return Err(conflict("只有暂停或停用的供应商可以恢复考察。"));}value["status"]=json!("考察中");},
            _=>return Err(invalid("业务动作未注册。")),
        }
        let identity=if resource.identity.is_empty(){None}else{Some(text(&value,resource.identity))};
        let saved=store::save(transaction,kind,id,value,identity,actor,action)?;
        if kind == "invoices" {
            transaction.append_invoice_status(&export_doc_storage::InvoiceStatusEvent {
                id: saved["versionNumber"].as_i64().ok_or_else(|| super::error::unavailable("发票版本缺失。"))?,
                invoice_id: id,
                from_status: state,
                to_status: text(&saved, "status"),
                note: text(&body, "note"),
                changed_by_user_id: actor.id,
                changed_by_username: text(&store::get(transaction, "users", actor.id)?, "username"),
                changed_at: text(&saved, "updatedAt"),
            })?;
            let dto: ApiInvoiceDetailDto = serde_json::from_value(saved)?;
            let invoice = InvoiceDraft::from_dto(dto).build().map_err(invalid)?;
            return Ok(json!({"success":true,"id":invoice.id,"isUpdate":true,"message":"操作完成","invoice":invoice}));
        }
        Ok(saved)
    })
}
