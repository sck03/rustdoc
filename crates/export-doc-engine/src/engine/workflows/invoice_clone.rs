use super::super::{
    catalog::Resource,
    error::{Result, invalid},
    records,
    store::Actor,
};
use crate::{generated_api::*, invoice::InvoiceDraft};
use export_doc_storage::Connection;
use rust_decimal::Decimal;
use serde_json::{Value, json};

pub(super) fn save(
    transaction: &Connection,
    actor: &Actor,
    resource: &Resource,
    operation: Operation,
    source: &Value,
    body: Value,
    business_date: chrono::NaiveDate,
) -> Result<Value> {
    let original: ApiInvoiceDetailDto = serde_json::from_value(source.clone())?;
    let as_type = operation == CLONE_INVOICE_AS_TYPE;
    let (invoice_no, target_type, options) = if as_type {
        let request: ApiInvoiceCloneTypeRequest = serde_json::from_value(body)
            .map_err(|cause| invalid(format!("生成发票参数无效：{cause}")))?;
        let target_type = request.target_type.trim().to_owned();
        if !matches!(target_type.as_str(), "报关数据" | "实际数据") {
            return Err(invalid("目标业务类型须为报关数据或实际数据。"));
        }
        if target_type == original.r#type.trim() {
            return Err(invalid("目标业务类型必须与源发票不同。"));
        }
        (
            original.invoice_no.clone(),
            Some(target_type),
            request.options,
        )
    } else {
        let request: ApiInvoiceCloneRequest = serde_json::from_value(body)
            .map_err(|cause| invalid(format!("复制发票参数无效：{cause}")))?;
        (
            request.new_invoice_no.trim().to_owned(),
            None,
            request.options,
        )
    };
    let today = business_date.to_string();
    let mut invoice = if options.copy_header.unwrap_or(true) {
        original.clone()
    } else {
        InvoiceDraft::new(&today).header
    };
    invoice.id = 0;
    invoice.invoice_no = invoice_no;
    invoice.status = "Draft".into();
    invoice.row_version.clear();
    invoice.pending_hs_feedback.clear();
    invoice.extra.clear();
    if let Some(target_type) = target_type {
        invoice.r#type = target_type;
    }
    if options.reset_dates.unwrap_or(!as_type) {
        invoice.invoice_date = today.clone();
        invoice.shipment_date = today;
    }
    invoice.items = if options.copy_items.unwrap_or(true) {
        original.items
    } else {
        vec![]
    };
    for item in &mut invoice.items {
        item.id = 0;
        item.invoice_id = 0;
        item.extra.clear();
        if options.clear_amounts.unwrap_or(false) {
            item.price_calculation_mode = "UnitPriceDriven".into();
            item.unit_price = Decimal::ZERO;
            item.total_price = Decimal::ZERO;
            item.purchase_price = Decimal::ZERO;
            item.purchase_total = Decimal::ZERO;
            item.tax_rebate_rate = Decimal::ZERO;
        }
    }
    let saved = records::save_in_transaction(
        transaction,
        actor,
        resource,
        0,
        &serde_json::to_value(invoice)?,
        business_date,
        as_type.then_some(source),
    )?;
    // Decode the response before committing: any invalid field rolls back the
    // new record and its audit entry, so a failed response cannot leave a copy.
    let invoice: ApiInvoiceDetailDto = serde_json::from_value(saved)?;
    let message = if as_type {
        format!("已生成同一发票号的{}。", invoice.r#type)
    } else {
        "发票已复制。".into()
    };
    Ok(json!({"success":true,"id":invoice.id,"invoice":invoice,"message":message}))
}
