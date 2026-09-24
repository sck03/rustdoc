use super::*;

fn attribute(node: &str, name: &str) -> f32 {
    node.split(&format!("{name}=\""))
        .nth(1)
        .unwrap()
        .split('"')
        .next()
        .unwrap()
        .parse()
        .unwrap()
}

fn text_y(svg: &str, text: &str) -> f32 {
    attribute(
        svg.split("<text ")
            .find(|node| node.contains(&format!(">{text}</text>")))
            .unwrap_or_else(|| panic!("Missing text: {text}")),
        "y",
    )
}

fn horizontal_between(svg: &str, start: f32, end: f32) -> bool {
    svg.split("<line ").skip(1).any(|node| {
        let y = attribute(node, "y1");
        (y - attribute(node, "y2")).abs() < 0.001 && y > start && y < end
    })
}

#[test]
fn commercial_rows_have_no_dividers_and_values_share_the_lower_baseline() {
    for template in [Builtin::Invoice, Builtin::PackingList] {
        let mut data = invoice(2);
        data.root["items"][0]["styleNo"] = json!("LOWER-1");
        data.root["items"][1]["styleNo"] = json!("LOWER-2");
        let document = render_builtin(template, &data, &AtomicBool::new(false)).unwrap();
        let svg = &document.pages[0].svg;
        let baseline = text_y(svg, "LOWER-1");
        for value in if template == Builtin::Invoice {
            vec!["20CTNS", "1000PCS", "@USD4.50", "USD 4500.00"]
        } else {
            vec!["20CTNS", "1000PCS", "250 KGS", "230 KGS", "1.2 CBM"]
        } {
            assert!(
                (baseline - text_y(svg, value)).abs() < 0.02,
                "{}: {value}, expected y={baseline}, actual y={}",
                template.label(),
                text_y(svg, value)
            );
        }
        let next = text_y(svg, "LOWER-2");
        let total_y = text_y(svg, "TOTAL:");
        let total_border = svg
            .split("<line ")
            .skip(1)
            .filter_map(|node| {
                let y = attribute(node, "y1");
                ((y - attribute(node, "y2")).abs() < 0.001 && y < total_y).then_some(y)
            })
            .fold(0.0_f32, f32::max);
        assert!(
            total_y - total_border >= 4.9,
            "TOTAL must retain its increased top padding"
        );
        assert!(
            !horizontal_between(svg, baseline, next),
            "{}",
            template.label()
        );
        let mut design = template.design().unwrap();
        if let Kind::Flow {
            block: ReportBlock::DetailTable(table),
            ..
        } = &mut design.layers[1].elements[0].kind
        {
            table.row_separators = Some(true);
        }
        let bordered =
            export_doc_report::render_design(&data, &design, &AtomicBool::new(false)).unwrap();
        assert!(horizontal_between(&bordered.pages[0].svg, baseline, next));
    }
}

#[test]
fn invoice_amount_stays_with_price_when_po_is_empty_or_style_wraps() {
    for (po, style) in [
        ("", "STYLE".to_owned()),
        ("PO-ALIGN", "VERY-LONG-STYLE-".repeat(12)),
    ] {
        let mut data = invoice(1);
        data.root["items"][0]["poNumber"] = json!(po);
        data.root["items"][0]["styleNo"] = json!(style);
        let document = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
        let svg = &document.pages[0].svg;
        assert!((text_y(svg, "@USD4.50") - text_y(svg, "USD 4500.00")).abs() < 0.02);
    }
}
