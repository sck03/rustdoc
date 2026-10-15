use crate::{Builtin, ReportData, render_design};
use export_doc_domain::{
    designer::{Design, DetailTable, Kind, ReportBlock},
    invoice::InvoiceDraft,
};
use serde_json::json;
use std::sync::atomic::AtomicBool;

fn table(design: &mut Design) -> &mut DetailTable {
    design
        .layers
        .iter_mut()
        .flat_map(|layer| &mut layer.elements)
        .find_map(|element| match &mut element.kind {
            Kind::Flow {
                block: ReportBlock::DetailTable(table),
                ..
            } => Some(table),
            _ => None,
        })
        .unwrap()
}

fn sample() -> ReportData {
    let invoice = InvoiceDraft::demo("2026-10-15", "DETAIL-REGRESSION")
        .build()
        .unwrap();
    let mut data = ReportData::invoice(&invoice, json!({}), json!({}), false).unwrap();
    data.root["items"] = json!([
        {"StyleName":"ITEM-A","PoNumber":"A","Quantity":1},
        {"StyleName":"ITEM-EMPTY","PoNumber":"","Quantity":2},
        {"StyleName":"ITEM-B","PoNumber":"B","Quantity":3}
    ]);
    data
}

fn coordinate(svg: &str, text: &str, axis: &str) -> f32 {
    let node = svg
        .split("<text ")
        .find(|node| node.contains(&format!(">{text}</text>")))
        .unwrap_or_else(|| panic!("missing {text}"));
    node.split(&format!("{axis}=\""))
        .nth(1)
        .unwrap()
        .split('"')
        .next()
        .unwrap()
        .parse()
        .unwrap()
}

#[test]
fn grouped_headers_repeat_and_fixed_cells_share_the_declared_merged_width() {
    let mut design = Builtin::Invoice.design().unwrap();
    let detail = table(&mut design);
    detail.columns[0].header_group_title = "Product group".into();
    detail.columns[0].header_group_span = 2;
    detail.print.first_page_rows = Some(1);
    detail.print.continuation_page_rows = Some(1);
    detail.print.fill_height = true;
    detail.intro_row = Some(serde_json::from_value(json!({"label":"Merged caption","labelColumnSpan":2,"style":{},"cells":[
        {"columnId":detail.columns[0].id,"contentKind":"Text","text":"Left note","fieldPath":""},
        {"columnId":detail.columns[1].id,"contentKind":"Text","text":"Inside note","fieldPath":""}
    ]})).unwrap());
    let document = render_design(&sample(), &design, &AtomicBool::new(false)).unwrap();
    assert!(document.pages.len() >= 3);
    for page in &document.pages {
        assert!(page.svg.contains(">Product group</text>"));
    }
    let first = &document.pages[0].svg;
    let merged_x = coordinate(first, "Merged caption", "x");
    assert!((coordinate(first, "Left note", "x") - merged_x).abs() < 0.01);
    assert!((coordinate(first, "Inside note", "x") - merged_x).abs() < 0.01);
    assert!(
        document
            .pages
            .iter()
            .skip(1)
            .all(|page| !page.svg.contains("Merged caption"))
    );
}

#[test]
fn empty_group_keys_have_their_own_subtotal_and_titles_follow_the_first_item() {
    let mut design = Builtin::Invoice.design().unwrap();
    let detail = table(&mut design);
    detail.print.first_page_rows = Some(1);
    detail.print.continuation_page_rows = Some(1);
    detail.grouping = Some(serde_json::from_value(json!({"fieldPath":"item.PoNumber","label":"GROUP","showFieldValue":true,"keepTogether":true,"pageBreakBefore":false,"style":{},
        "footer":{"label":"SUB","labelColumnSpan":2,"style":{},"cells":[{"columnId":detail.columns.last().unwrap().id,"contentKind":"Sum","text":"","fieldPath":"item.Quantity"}]}
    })).unwrap());
    let document = render_design(&sample(), &design, &AtomicBool::new(false)).unwrap();
    for (group, item, sum) in [
        ("A", "ITEM-A", "1"),
        ("", "ITEM-EMPTY", "2"),
        ("B", "ITEM-B", "3"),
    ] {
        let title = if group.is_empty() {
            "GROUP".into()
        } else {
            format!("GROUP {group}")
        };
        let subtotal = if group.is_empty() {
            "SUB".into()
        } else {
            format!("SUB {group}")
        };
        let page = document
            .pages
            .iter()
            .find(|page| page.svg.contains(&format!(">{title}</text>")))
            .unwrap();
        assert!(
            page.svg.contains(item),
            "group title must share a page with its first item"
        );
        let y = coordinate(&page.svg, &subtotal, "y");
        assert!(
            page.svg
                .split("<text ")
                .any(|node| node.contains(&format!(">{sum}</text>"))
                    && node.split("y=\"").nth(1).is_some_and(|value| value
                        .split('"')
                        .next()
                        .unwrap()
                        .parse::<f32>()
                        .is_ok_and(|value| (value - y).abs() < 0.01)))
        );
    }
}
