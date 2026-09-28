//! Synthetic data uses the same DTO calculations and native layout as invoices.
use super::*;
use rust_decimal::Decimal;

pub(super) fn data(kind: &str, profile: &str, date: &str, with_seal: bool) -> Result<ReportData> {
    if kind == "PaymentVoucher" {
        if !["", "apiSample", "paymentVoucher"].contains(&profile) {
            return Err(invalid("付款报表样例类型无效。"));
        }
        let mut value = contracts::overlay(
            contracts::initial(contracts::schema("ApiPaymentDto")),
            &json!({"payerName":"示例公司","payeeName":"示例收款单位","department":"业务部","project":"费用支付","cnyAmount":12345.67,"usdAmount":100,"invoiceNo":"PREVIEW-001","paymentDate":date,"shipmentDate":date,"paymentMethod":"电汇","accountNo":"6222 0200 0000 0000"}),
        );
        add_spares(&mut value);
        return ReportData::payment(&serde_json::from_value(value)?, json!({})).map_err(Into::into);
    }
    if ![
        "",
        "apiSample",
        "exportStandard",
        "exportImageMarks",
        "exportLongItems",
    ]
    .contains(&profile)
    {
        return Err(invalid("发票报表样例类型无效。"));
    }
    let mut invoice = InvoiceDraft::demo(date, "PREVIEW-001")
        .build()
        .map_err(invalid)?;
    if !["", "apiSample"].contains(&profile) {
        let count = if profile == "exportLongItems" { 72 } else { 8 };
        let seed = invoice.items[0].clone();
        invoice.items = (1..=count)
            .map(|number| {
                let mut item = seed.clone();
                item.po_number = format!("PO-{number:03}");
                item.style_name = format!("Sample product {number:02} with controlled wrapping");
                item.style_no = if profile == "exportLongItems" {
                    format!("SKU-{number:03}-LONG-CODE-{}", "X".repeat(18))
                } else {
                    format!("SKU-{number:03}-STD")
                };
                item.quantity = Decimal::from(10 + number);
                item.cartons = Decimal::from(2 + number % 5);
                item.ctn_unit_en = "CTNS".into();
                item.unit_en = "PCS".into();
                item.unit_price = Decimal::new(58 + number, 1);
                item.total_price = item.quantity * item.unit_price;
                item
            })
            .collect();
        invoice = InvoiceDraft::from_dto(invoice).build().map_err(invalid)?;
    }
    let mut data = ReportData::invoice(&invoice, json!({}), json!({}), with_seal)?;
    add_spares(&mut data.root["Invoice"]);
    for item in data.root["items"].as_array_mut().into_iter().flatten() {
        add_spares(item);
    }
    if profile == "exportImageMarks" {
        data.images.insert(
            "Invoice.ShippingMarks".into(),
            render::RasterImage {
                media_type: "image/png".into(),
                bytes: include_bytes!("samples-shipping-marks.png").to_vec(),
            },
        );
    }
    Ok(data)
}

fn add_spares(value: &mut Value) {
    for index in 1..=10 {
        value[format!("spare{index}")] = json!(format!("备用 {index} 示例"));
    }
}
