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

// The last-page totals are positioned with a following-footer group transform.
fn nodes<'a>(svg: &'a str, tag: &str) -> Vec<(&'a str, f32)> {
    let mut stack = vec![0.];
    let mut result = Vec::new();
    for node in svg.split('<').skip(1) {
        if node.starts_with("g ") {
            let offset = node
                .split("transform=\"translate(0 ")
                .nth(1)
                .and_then(|v| v.split(')').next())
                .and_then(|v| v.parse::<f32>().ok())
                .unwrap_or(0.);
            stack.push(stack.last().unwrap() + offset);
        } else if node.starts_with("/g>") {
            stack.pop();
        } else if node.starts_with(&format!("{tag} ")) {
            result.push((node, *stack.last().unwrap()));
        }
    }
    result
}

fn text_y(svg: &str, text: &str) -> f32 {
    nodes(svg, "text")
        .into_iter()
        .filter(|(node, _)| node.ends_with(&format!(">{text}")))
        .map(|(node, offset)| attribute(node, "y") + offset)
        .min_by(f32::total_cmp)
        .unwrap_or_else(|| panic!("Missing text: {text}"))
}

fn horizontal_between(svg: &str, start: f32, end: f32) -> bool {
    nodes(svg, "line").into_iter().any(|(node, offset)| {
        let y = attribute(node, "y1") + offset;
        (y - attribute(node, "y2") - offset).abs() < 0.001 && y > start && y < end
    })
}

#[test]
fn commercial_headers_and_packing_frame_survive_paging_and_roundtrip() {
    for template in [Builtin::Invoice, Builtin::PackingList] {
        let source = serde_json::to_string(&template.design().unwrap()).unwrap();
        let design = export_doc_domain::designer::Design::from_source(&source).unwrap();
        for count in [1, 36] {
            let data = invoice(count);
            let document =
                export_doc_report::render_design(&data, &design, &AtomicBool::new(false)).unwrap();
            for page in &document.pages {
                for label in ["Invoice No.:", "Contract No.:", "Date:"] {
                    let node = nodes(&page.svg, "text")
                        .into_iter()
                        .find(|(n, _)| n.ends_with(&format!(">{label}")))
                        .unwrap()
                        .0;
                    assert!((attribute(node, "x") - 161.7).abs() < 0.02);
                    assert!(node.contains("text-anchor=\"end\""));
                }
                if template == Builtin::PackingList {
                    for x in [15., 47., 195.] {
                        assert!(
                            nodes(&page.svg, "line")
                                .into_iter()
                                .any(|(n, _)| (attribute(n, "x1") - x).abs() < 0.01
                                    && (attribute(n, "x2") - x).abs() < 0.01
                                    && (attribute(n, "y2") - 245.).abs() < 0.01
                                    && attribute(n, "y1") < 80.),
                            "packing column {x} must reach the frame bottom"
                        );
                    }
                    assert!(
                        !nodes(&page.svg, "line").into_iter().any(|(n, _)| {
                            let x = attribute(n, "x1");
                            x > 47.01
                                && x < 194.99
                                && (x - attribute(n, "x2")).abs() < 0.01
                                && (attribute(n, "y1") - attribute(n, "y2")).abs() > 0.01
                        }),
                        "packing must not draw internal goods-column separators"
                    );
                }
            }
            let last = &document.pages.last().unwrap().svg;
            if template == Builtin::PackingList {
                for (field, unit) in [
                    ("Invoice.TotalGrossWeight", "KGS"),
                    ("Invoice.TotalNetWeight", "KGS"),
                    ("Invoice.TotalVolume", "CBM"),
                ] {
                    let text = format!(">{}{unit}", data.text(field));
                    assert!(
                        nodes(last, "text")
                            .iter()
                            .any(|(n, _)| n.ends_with(&text) && n.contains("font-weight=\"700\"")),
                        "packing total must include its unit: {text}"
                    );
                }
            }
            let total = text_y(last, "TOTAL:");
            assert!(nodes(last, "line").into_iter().any(|(n, offset)| {
                n.contains("stroke-dasharray=\"1.1 0.7\"")
                    && (attribute(n, "x1") - 47.).abs() < 0.01
                    && (attribute(n, "x2") - 195.).abs() < 0.01
                    && total - attribute(n, "y1") - offset > 0.
                    && total - attribute(n, "y1") - offset < 10.
            }));
        }
    }
}

#[test]
fn commercial_rows_have_no_dividers_and_values_share_the_lower_baseline() {
    for template in [Builtin::Invoice, Builtin::PackingList] {
        let mut data = invoice(2);
        data.root["items"][0]["styleNo"] = json!("LOWER-1");
        data.root["items"][1]["styleNo"] = json!("LOWER-2");
        let document = render_builtin(template, &data, &AtomicBool::new(false)).unwrap();
        let svg = &document.pages[0].svg;
        assert!(
            !svg.contains("#f2f2f2"),
            "commercial headers must have a white background"
        );
        let marks = nodes(svg, "text")
            .into_iter()
            .find(|(node, _)| node.ends_with(">唛头 / Marks"))
            .unwrap()
            .0;
        assert!(marks.contains("font-weight=\"700\""));
        if template == Builtin::PackingList {
            assert!(
                (text_y(svg, "唛头 / Marks") - text_y(svg, "货品名称 / Description")).abs() < 0.02
            );
            let header_bottom = nodes(svg, "line").into_iter().find(|(node, offset)| {
                let y = attribute(node, "y1") + offset;
                attribute(node, "x1") == 15.
                    && attribute(node, "x2") == 47.
                    && y > text_y(svg, "唛头 / Marks")
                    && y < text_y(svg, "LOWER-1")
            });
            assert!(
                header_bottom.is_some(),
                "marks header needs its own bottom rule"
            );
        }
        let baseline = text_y(svg, "LOWER-1");
        for value in if template == Builtin::Invoice {
            vec!["20", "CTNS", "1000", "PCS", "USD4.50", "USD4500.00"]
        } else {
            vec!["20CTNS", "1000PCS", "250KGS", "230KGS", "1.2CBM"]
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
        let total_border = nodes(svg, "line")
            .into_iter()
            .filter_map(|(node, offset)| {
                let y = attribute(node, "y1") + offset;
                ((y - attribute(node, "y2") - offset).abs() < 0.001 && y < total_y).then_some(y)
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
        if template == Builtin::PackingList {
            assert!(horizontal_between(&bordered.pages[0].svg, baseline, next));
        }
    }
}

#[test]
fn invoice_amount_header_rule_is_not_covered_by_document_fields() {
    for count in [1, 36] {
        let mut data = invoice(count);
        data.root["Invoice"]["tradeTerms"] = json!("FOB");
        let document = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
        for page in &document.pages {
            let svg = &page.svg;
            let (rule, _) = nodes(svg, "line")
                .into_iter()
                .find(|(node, offset)| {
                    (attribute(node, "x1") - 162.).abs() < 0.01
                        && (attribute(node, "x2") - 195.).abs() < 0.01
                        && (attribute(node, "y1") + offset - 86.).abs() < 0.01
                        && (attribute(node, "y2") + offset - 86.).abs() < 0.01
                })
                .expect("amount header must have a complete bottom rule");
            let rule_position = svg.find(rule).unwrap();
            for (rect, offset) in nodes(svg, "rect") {
                if svg.find(rect).unwrap() < rule_position {
                    continue;
                }
                let x = attribute(rect, "x");
                let y = attribute(rect, "y") + offset;
                let covers = x < 195.
                    && x + attribute(rect, "width") > 162.
                    && y < 86.1
                    && y + attribute(rect, "height") > 85.9;
                assert!(
                    !covers,
                    "a later field background obscures the amount header rule"
                );
            }
        }
    }
}

#[test]
fn invoice_amount_stays_with_price_when_po_is_empty_or_style_wraps() {
    for (po, style) in [
        ("", "STYLE".to_owned()),
        ("PO-ALIGN", "VERY-LONG-STYLE-".repeat(12)),
    ] {
        let mut data = invoice(1);
        data.root["Invoice"]["totalAmount"] = json!(9000);
        data.root["items"][0]["poNumber"] = json!(po);
        data.root["items"][0]["styleNo"] = json!(style);
        let document = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
        let svg = &document.pages[0].svg;
        assert!((text_y(svg, "USD4.50") - text_y(svg, "USD4500.00")).abs() < 0.02);
    }
}

#[test]
fn independent_invoice_fields_and_totals_share_columns_and_total_baseline() {
    let data = invoice(2);
    let document = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
    let svg = &document.pages[0].svg;
    let x = |value: &str| {
        attribute(
            nodes(svg, "text")
                .into_iter()
                .find(|(node, _)| node.ends_with(&format!(">{value}")))
                .unwrap()
                .0,
            "x",
        )
    };
    for (detail, unit, total, anchor) in [("20", "CTNS", "40", 95.), ("1000", "PCS", "2000", 120.)]
    {
        assert!(
            (x(unit) - x(detail)).abs() < 0.01,
            "numbers and units must be joined without a gap"
        );
        assert!((x(total) - anchor).abs() < 0.01);
        assert!((x(total) - x(detail)).abs() < 0.01);
        assert!((text_y(svg, "TOTAL:") - text_y(svg, total)).abs() < 0.01);
    }
    assert!((x("USD4500.00") - x("USD9000.00")).abs() < 0.01);
    assert!((text_y(svg, "TOTAL:") - text_y(svg, "USD9000.00")).abs() < 0.01);
    assert!(
        !nodes(svg, "text")
            .iter()
            .any(|(node, _)| node.ends_with(">USD"))
    );
    let design = Builtin::Invoice.design().unwrap();
    assert!(
        design
            .layers
            .iter()
            .flat_map(|layer| &layer.elements)
            .all(|e| !matches!(
                &e.kind,
                Kind::Flow {
                    block: ReportBlock::DetailTable(_),
                    ..
                }
            ))
    );
    for path in [
        "item.StyleName",
        "item.Cartons",
        "item.Quantity",
        "item.UnitPrice",
        "item.TotalPrice",
    ] {
        assert!(
            design
                .layers
                .iter()
                .flat_map(|layer| &layer.elements)
                .any(|e| !e.locked
                    && matches!(&e.kind, Kind::Field {field_path,..} if field_path == path))
        );
    }
}

#[test]
fn invoice_grouped_totals_keep_each_number_and_unit_on_the_same_line() {
    let mut draft = InvoiceDraft::demo("2026-09-16", "UNIT-TOTALS")
        .build()
        .unwrap();
    draft.items[1].ctn_unit_en = "BOXES".into();
    draft.items[1].unit_en = "SETS".into();
    let data = ReportData::invoice(&draft, json!({}), json!({}), false).unwrap();
    assert_eq!(data.text("total_by_ctn_unit.Value"), "12\n28");
    assert_eq!(data.text("total_by_ctn_unit.Key"), "BOXES\nCTNS");
    assert_eq!(data.text("total_by_qty_unit.Value"), "1400\n600");
    assert_eq!(data.text("total_by_qty_unit.Key"), "PCS\nSETS");
    let rendered = render_builtin(Builtin::Invoice, &data, &AtomicBool::new(false)).unwrap();
    let svg = &rendered.pages.last().unwrap().svg;
    let totals: Vec<_> = nodes(svg, "text")
        .into_iter()
        .filter(|(node, _)| node.contains("font-weight=\"700\""))
        .collect();
    for (number, unit, anchor) in [
        ("12", "BOXES", 95.),
        ("28", "CTNS", 95.),
        ("1400", "PCS", 120.),
        ("600", "SETS", 120.),
    ] {
        let (number_node, number_offset) = totals
            .iter()
            .find(|(node, _)| node.ends_with(&format!(">{number}")))
            .unwrap();
        let (unit_node, unit_offset) = totals
            .iter()
            .find(|(node, _)| node.ends_with(&format!(">{unit}")))
            .unwrap();
        assert!((attribute(number_node, "x") - anchor).abs() < 0.01);
        assert!((attribute(unit_node, "x") - anchor).abs() < 0.01);
        assert!(
            (attribute(number_node, "y") + number_offset - attribute(unit_node, "y") - unit_offset)
                .abs()
                < 0.01
        );
    }
}
