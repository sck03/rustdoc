use super::*;
#[path = "../tests/support/coo_template.rs"]
mod fixture;

#[test]
fn coo_collection_template_omits_operator_only_fields() {
    let bytes = fixture::filled(&[]);
    let preview =
        preview_single_window(&bytes, "optional.xlsx", None, "2026-11-12", &|| Ok(())).unwrap();
    assert!(preview.result.success, "{preview:#?}");
    let invoice = preview.result.invoice.unwrap();
    assert_eq!(invoice.items.len(), 2);
    assert!(invoice.items[0].style_no.is_empty());
    assert_eq!(invoice.items[0].gw_total.to_string(), "0");
    assert_eq!(invoice.items[0].nw_total.to_string(), "0");
}

#[test]
fn coo_template_preserves_factory_identity_decimal_fob_and_blank_row_alignment() {
    let result = preview_single_window(
        &fixture::filled(&[]),
        "customer.xlsx",
        None,
        "2026-11-11",
        &|| Ok(()),
    )
    .unwrap();
    assert!(result.result.success, "{result:#?}");
    let invoice = result.result.invoice.as_ref().unwrap();
    assert_eq!(invoice.invoice_no, "COO-001");
    assert_eq!(invoice.invoice_date, "2026-11-11");
    assert_eq!(invoice.items.len(), 2);
    assert_eq!(invoice.items[0].style_name, "COTTON SHIRTS");
    assert_eq!(invoice.items[0].origin, "CHINA");
    assert_eq!(invoice.items[1].origin, "CHINA");
    assert_eq!(invoice.items[0].total_price.to_string(), "101.25");
    let coo = serde_json::to_value(result.coo.as_ref().unwrap()).unwrap();
    assert_eq!(coo["items"][0]["ciqRegNo"], "012345678");
    assert_eq!(coo["items"][0]["fobValue"], "90.12345");
    assert_eq!(
        coo["items"][0]["producer"],
        "TEST FACTORY ONE LTD.\n3 FACTORY ROAD, CHINA"
    );
    assert_eq!(coo["items"][1]["prdcEtpsConcEr"], "李四");
    export_doc_contracts::validation::response(
        "PreviewSingleWindowExcelImport",
        &serde_json::to_value(result).unwrap(),
    )
    .unwrap();
}

#[test]
fn coo_origin_uses_the_declared_default_or_stays_unknown() {
    for origin in ["THAILAND", ""] {
        let result = preview_single_window(
            &fixture::filled(&[(1, "B22", origin)]),
            "origin.xlsx",
            None,
            "2026-11-11",
            &|| Ok(()),
        )
        .unwrap();
        assert!(result.result.success);
        let invoice = result.result.invoice.unwrap();
        assert_eq!(invoice.items[0].origin, origin);
        assert_eq!(invoice.items[1].origin, origin);
    }
}

#[test]
fn coo_units_translate_from_collected_english() {
    for (english, expected) in [("KGS", "千克"), ("PCS", "件"), ("", "")] {
        let result = preview_single_window(
            &fixture::filled(&[(2, "E4", english)]),
            "units.xlsx",
            None,
            "2026-11-11",
            &|| Ok(()),
        )
        .unwrap();
        assert!(result.result.success);
        let invoice = result.result.invoice.unwrap();
        assert_eq!(invoice.items[0].unit_en, english);
        assert_eq!(invoice.items[0].unit_cn, expected);
    }
}

#[test]
fn coo_import_rejects_broken_links_headers_dates_and_amounts() {
    for (changes, reason) in [
        (vec![(2, "K4", "missing")], "不存在"),
        (vec![(3, "A5", "F001")], "重复"),
        (vec![(3, "A4", "")], "缺少企业编号"),
        (vec![(1, "B4", "2026-02-30")], "YYYY-MM-DD"),
        (vec![(2, "J4", "-1")], "非负"),
        (vec![(2, "J4", "1.123456")], "五位"),
        (vec![(3, "E4", "123456789012345678")], "10"),
        (vec![(2, "K3", "changed")], "表头"),
    ] {
        let error = preview_single_window(
            &fixture::filled(&changes),
            "customer.xlsx",
            None,
            "2026-11-11",
            &|| Ok(()),
        )
        .unwrap_err();
        assert!(error.contains(reason), "{error}");
    }
    let empty = preview_single_window(fixture::TEMPLATE, "empty.xlsx", None, "2026-11-11", &|| {
        Ok(())
    })
    .unwrap();
    assert!(!empty.result.success);
    let invalid = preview_single_window(
        &fixture::filled(&[(2, "D4", "")]),
        "partial.xlsx",
        None,
        "2026-11-11",
        &|| Ok(()),
    )
    .unwrap();
    assert!(!invalid.result.success);
}
