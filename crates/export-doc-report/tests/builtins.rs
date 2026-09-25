use export_doc_contracts::generated_api::ApiPaymentDto;
use export_doc_domain::{
    designer::{Kind, ReportBlock},
    invoice::InvoiceDraft,
    report_template_format,
};
use export_doc_report::{BUILTINS, Builtin, ErrorKind, ReportData, render_builtin};
use serde_json::json;
use std::sync::atomic::AtomicBool;
#[path = "support/commercial_alignment.rs"]
mod commercial_alignment;

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

fn detail_table(template: Builtin) -> export_doc_domain::designer::DetailTable {
    template
        .design()
        .unwrap()
        .layers
        .into_iter()
        .flat_map(|layer| layer.elements)
        .find_map(|element| match element.kind {
            Kind::Flow {
                block: ReportBlock::DetailTable(table),
                ..
            } => Some(table),
            _ => None,
        })
        .unwrap()
}

#[test]
fn every_builtin_is_a_inspectable_dtpl_v3_document() {
    for template in BUILTINS {
        let design = template.design().unwrap();
        assert_eq!(design.version, 3, "{}", template.label());
        assert_eq!(design.ast_kind, "ReportDocument", "{}", template.label());
        assert_eq!(design.contract_version, "3.0", "{}", template.label());
        assert_eq!(
            design.report_type,
            template.report_type(),
            "{}",
            template.label()
        );
        assert!(
            template.source().starts_with(b"EXPORTDOCDT"),
            "{}",
            template.label()
        );
        assert_eq!(
            design,
            report_template_format::decode(template.source()).unwrap(),
            "{}",
            template.label()
        );
        assert!(
            design
                .layers
                .iter()
                .any(|layer| layer.role == "Body" && layer.visible)
        );
    }
}

#[test]
fn every_builtin_renders_its_own_domain_without_html_execution() {
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
        assert!(document.html().unwrap().contains("@page report0"));
    }
}

#[test]
fn commercial_details_and_customs_continuation_use_shared_pagination() {
    for template in [Builtin::PackingList] {
        let table = detail_table(template);
        assert_eq!(table.print.first_page_rows, Some(12));
        assert_eq!(table.print.continuation_page_rows, Some(12));
    }
    let customs = detail_table(Builtin::CustomsDeclaration);
    assert_eq!(customs.print.first_page_rows, Some(6));
    assert_eq!(customs.print.continuation_page_rows, Some(15));
}

#[test]
fn invoice_terms_and_packing_header_follow_their_separate_templates() {
    let data = invoice(36);
    let document = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
    assert!(document.pages[0].svg.contains(">FOB</text>"));
    assert!(
        document
            .pages
            .iter()
            .skip(1)
            .all(|page| !page.svg.contains(">FOB</text>"))
    );
    assert!(document.pages[0].svg.contains("Issued by:"));
    let position = |text: &str| {
        let node = document.pages[0]
            .svg
            .split("<text ")
            .find(|node| node.contains(&format!(">{text}</text>")))
            .unwrap();
        node.split("x=\"")
            .nth(1)
            .unwrap()
            .split('"')
            .next()
            .unwrap()
            .parse::<f32>()
            .unwrap()
    };
    assert!(position("1000") - position("20") > 15.);
    assert!(position("4.50") - position("1000") > 15.);
    let packing =
        render_builtin(Builtin::PackingList, &invoice(3), &AtomicBool::new(false)).unwrap();
    for label in ["From:", "Payment Terms:", "Issued by:"] {
        assert!(!packing.pages[0].svg.contains(label));
    }
}

#[test]
fn multipage_builtins_repeat_only_the_required_bands() {
    let mut data = invoice(36);
    data.root["Invoice"]["shippingMarks"] = json!("FIRST-PAGE-MARKS");
    for template in [Builtin::Invoice, Builtin::PackingList] {
        let result = render_builtin(template, &data, &AtomicBool::new(false)).unwrap();
        assert_eq!(
            result.pages.len(),
            if template == Builtin::Invoice { 4 } else { 3 },
            "{}",
            template.label()
        );
        assert!(result.pages[0].svg.contains("FIRST-PAGE-MARKS"));
        assert!(
            result
                .pages
                .iter()
                .skip(1)
                .all(|p| !p.svg.contains("FIRST-PAGE-MARKS"))
        );
        assert!(
            result
                .pages
                .iter()
                .take(result.pages.len() - 1)
                .all(|p| !p.svg.contains("TOTAL:"))
        );
        assert!(result.pages.last().unwrap().svg.contains("TOTAL:"));
        for index in 1..=36 {
            let all = result
                .pages
                .iter()
                .map(|page| page.svg.as_str())
                .collect::<String>();
            assert_eq!(all.matches(&format!(">STYLE-{index}</text>")).count(), 1);
        }
    }
    let contract = render_builtin(Builtin::Contract, &data, &AtomicBool::new(false)).unwrap();
    assert_eq!(contract.pages.len(), 2);
    assert!(contract.pages[0].svg.contains("售货合同"));
    assert!(!contract.pages[1].svg.contains("售货合同"));
    assert!(!contract.pages[0].svg.contains("The buyers"));
    assert!(contract.pages[1].svg.contains("The buyers"));
    let customs =
        render_builtin(Builtin::CustomsDeclaration, &data, &AtomicBool::new(false)).unwrap();
    assert_eq!(customs.pages.len(), 3);
    assert!(customs.pages[0].svg.contains("特殊关系确认"));
    assert!(
        customs
            .pages
            .iter()
            .skip(1)
            .all(|p| !p.svg.contains("特殊关系确认") && !p.svg.contains("境内发货人"))
    );
}

#[test]
fn empty_or_hidden_po_and_style_do_not_leave_blank_composite_lines() {
    let mut data = invoice(1);
    data.root["items"][0]["poNumber"] = json!("");
    data.root["items"][0]["styleNo"] = json!("");
    let design = Builtin::PackingList.design().unwrap();
    let no_optional =
        export_doc_report::render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    assert!(no_optional.pages[0].svg.contains("20CTNS"));
    let mut hidden = design.clone();
    if let Kind::Flow {
        block: ReportBlock::DetailTable(table),
        ..
    } = &mut hidden.layers[1].elements[0].kind
    {
        for part in &mut table.columns[0].content {
            if ["item.PoNumber", "item.StyleNo"].contains(&part.field_path.as_str()) {
                part.visible = Some(false);
            }
        }
    }
    data.root["items"][0]["poNumber"] = json!("HIDDEN-PO");
    data.root["items"][0]["styleNo"] = json!("HIDDEN-STYLE");
    let hidden_result =
        export_doc_report::render_design(&data, &hidden, &AtomicBool::new(false)).unwrap();
    assert_eq!(no_optional.pages[0].svg, hidden_result.pages[0].svg);
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
