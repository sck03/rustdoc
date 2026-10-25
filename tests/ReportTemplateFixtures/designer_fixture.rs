//! Anonymous synthetic design used by Domain, report and workflow regressions.
pub fn invoice() -> export_doc_domain::designer::Design {
    serde_json::from_str(include_str!("synthetic-invoice-v3.json"))
        .expect("valid synthetic V3 design fixture")
}
