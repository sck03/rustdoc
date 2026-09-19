use export_doc_contracts::generated_api::ApiPaymentDto;
use export_doc_domain::invoice::InvoiceDraft;
use export_doc_report::{BUILTINS, Builtin, ErrorKind, ReportData, render_builtin};
use serde_json::json;
use std::sync::atomic::AtomicBool;

fn invoice(rows: usize) -> ReportData {
    let mut draft = InvoiceDraft::demo("2026-09-16", "REPORT-<>&-001");
    let row = draft.rows[0].clone();
    draft.rows = (0..rows)
        .map(|index| {
            let mut row = row.clone();
            row.cells[2] = format!("STYLE-{}", index + 1);
            row
        })
        .collect();
    ReportData::invoice(&draft.build().unwrap(), json!({}), json!({}), false).unwrap()
}

#[test]
fn every_builtin_renders_its_own_fields_and_paper_without_html_execution() {
    let invoice = invoice(3);
    let payment = ReportData::payment(
        &ApiPaymentDto {
            payment_date: Some("2026-09-16".into()),
            shipment_date: Some("2026-09-17".into()),
            cny_amount: "1234.56".parse().unwrap(),
            notes: "报销备注<&>".into(),
            ..Default::default()
        },
        json!({}),
    )
    .unwrap();
    for template in BUILTINS {
        let data = if template.report_type() == "PaymentVoucher" {
            &payment
        } else {
            &invoice
        };
        let document = render_builtin(template, data, &AtomicBool::new(false))
            .unwrap_or_else(|error| panic!("{}: {error:?}", template.label()));
        assert_eq!(document.pages.len(), 1, "{}", template.label());
        let page = &document.pages[0];
        assert_eq!(
            (page.width_mm, page.height_mm),
            if template == Builtin::CustomsDeclaration {
                (297., 210.)
            } else {
                (210., 297.)
            }
        );
        assert!(!page.svg.contains("{{"));
        assert!(!page.svg.contains("<script"));
        if template.report_type() == "PaymentVoucher" {
            let text = page
                .svg
                .split('>')
                .filter_map(|part| part.split_once('<').map(|(text, _)| text))
                .collect::<String>();
            assert!(text.contains("壹仟贰佰叁拾肆元伍角陆分"));
            assert!(text.contains("报销备注&lt;&amp;&gt;"));
        }
        assert!(document.html().unwrap().contains("@page report0"));
    }
}

#[test]
fn invoice_and_packing_list_keep_twelve_items_per_page_and_one_total() {
    for template in [Builtin::Invoice, Builtin::PackingList] {
        let document = render_builtin(template, &invoice(36), &AtomicBool::new(false)).unwrap();
        assert_eq!(document.pages.len(), 3);
        assert!(document.pages[0].svg.contains("STYLE-12"));
        assert!(!document.pages[0].svg.contains("STYLE-13"));
        assert!(document.pages[2].svg.contains("STYLE-36"));
        assert!(!document.pages[0].svg.contains("TOTAL:"));
        assert!(document.pages[2].svg.contains("TOTAL:"));
    }
}

#[test]
fn customs_continuation_keeps_all_items_after_the_first_six() {
    let document = render_builtin(
        Builtin::CustomsDeclaration,
        &invoice(37),
        &AtomicBool::new(false),
    )
    .unwrap();
    assert_eq!(document.pages.len(), 4);
    assert!(document.pages[3].svg.contains(">37</text>"));
    assert!(document.pages[0].svg.contains("境外品牌"));
}

#[test]
fn snapshots_override_master_data_without_case_alias_duplicates() {
    let mut draft = InvoiceDraft::demo("2026-09-16", "REPORT-SNAPSHOT");
    draft.header.exporter_credit_code = "TEST-CREDIT-001".into();
    let data = ReportData::invoice(
        &draft.build().unwrap(),
        json!({"customerNameEN":"changed customer"}),
        json!({"exporterNameEN":"changed exporter","creditCode":"changed code"}),
        false,
    )
    .unwrap();
    assert_eq!(
        data.text("Customer.CustomerNameEN"),
        draft.header.customer_name_en
    );
    assert_eq!(
        data.text("Exporter.ExporterNameEN"),
        draft.header.exporter_name_en
    );
    assert_eq!(data.text("Exporter.CreditCode"), "TEST-CREDIT-001");
}

#[test]
fn long_payment_notes_continue_without_losing_text_or_signatures() {
    let payment = ReportData::payment(
        &ApiPaymentDto {
            notes: format!("{}END-NOTES", "备注内容，核对费用。\n".repeat(150)),
            ..Default::default()
        },
        json!({}),
    )
    .unwrap();
    for template in [Builtin::PaymentVoucher, Builtin::ExpenseReimbursement] {
        let document = render_builtin(template, &payment, &AtomicBool::new(false)).unwrap();
        assert!(document.pages.len() > 1);
        let last = &document.pages.last().unwrap().svg;
        assert!(last.contains("END-NOTES"));
        assert!(last.contains(if template == Builtin::PaymentVoucher {
            "复核:"
        } else {
            "审批签字:"
        }));
    }
}

#[test]
fn cancellation_and_wrong_data_domain_fail_before_rendering() {
    let data = invoice(1);
    assert_eq!(
        render_builtin(Builtin::Invoice, &data, &AtomicBool::new(true))
            .err()
            .unwrap()
            .kind,
        ErrorKind::Cancelled
    );
    assert_eq!(
        render_builtin(Builtin::PaymentVoucher, &data, &AtomicBool::new(false))
            .err()
            .unwrap()
            .kind,
        ErrorKind::Invalid
    );
}
