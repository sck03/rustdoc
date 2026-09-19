//! Development evidence uses fictional data; reference/customer PDFs remain outside Git.
use export_doc_contracts::generated_api::ApiPaymentDto;
use export_doc_domain::invoice::InvoiceDraft;
use export_doc_report::{BUILTINS, Builtin, ReportData, pdf_document, render_builtin};
use serde_json::json;
use std::{path::PathBuf, sync::atomic::AtomicBool};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut args = std::env::args_os().skip(1);
    let font = PathBuf::from(
        args.next()
            .ok_or("Pass the governed font path and evidence output directory.")?,
    );
    let output = PathBuf::from(
        args.next()
            .ok_or("Pass an explicit evidence output directory.")?,
    );
    std::fs::create_dir_all(&output)?;
    let mut draft = InvoiceDraft::demo("2026-09-16", "REPORT-VALIDATION-001");
    draft.header.exporter_credit_code = "TEST-CREDIT-001".into();
    draft.header.shipping_marks = "CLIENT\nBRAND\nCARTON #\nDESCRIPTION\nPO\nSTYLE, COLOR, SIZE\nTOTAL UNITS\nCOO\nCARTON WEIGHT\nCARTON DIMENSIONS\nUPC".into();
    let invoice = ReportData::invoice(&draft.build()?, json!({}), json!({}), false)?;
    let payment = ReportData::payment(
        &ApiPaymentDto {
            payment_date: Some("2026-09-16".into()),
            shipment_date: Some("2026-09-16".into()),
            ..Default::default()
        },
        json!({}),
    )?;
    let cancelled = AtomicBool::new(false);
    let mut summary = vec![];
    for (index, template) in BUILTINS.into_iter().enumerate() {
        let data = if template.report_type() == "PaymentVoucher" {
            &payment
        } else {
            &invoice
        };
        let document = render_builtin(template, data, &cancelled)?;
        let name = format!("builtin-{}", index + 1);
        std::fs::write(output.join(format!("{name}.html")), document.html()?)?;
        std::fs::write(
            output.join(format!("{name}.pdf")),
            pdf_document(&document, &font, &cancelled)?,
        )?;
        summary.push(json!({"template":template.label(),"file":format!("{name}.pdf"),"pages":document.pages.len()}));
    }
    let row = draft.rows[0].clone();
    draft.rows = (0..36)
        .map(|i| {
            let mut row = row.clone();
            row.cells[2] = format!("STYLE-{}", i + 1);
            row
        })
        .collect();
    let data = ReportData::invoice(&draft.build()?, json!({}), json!({}), false)?;
    let document = render_builtin(Builtin::Invoice, &data, &cancelled)?;
    std::fs::write(
        output.join("invoice-36-items.pdf"),
        pdf_document(&document, &font, &cancelled)?,
    )?;
    summary.push(json!({"template":"36 行商业发票","file":"invoice-36-items.pdf","pages":document.pages.len()}));
    std::fs::write(
        output.join("summary.json"),
        serde_json::to_vec_pretty(&summary)?,
    )?;
    println!("{}", serde_json::to_string_pretty(&summary)?);
    Ok(())
}
