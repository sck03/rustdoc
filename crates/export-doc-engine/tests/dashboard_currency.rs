use export_doc_engine::{generated_api::*, invoice::InvoiceDraft};
use serde_json::{Value, json};
#[path = "support/native_fixture.rs"]
mod native_fixture;
#[test]
fn stored_invoice_query_matches_the_domain_search_rules() {
    let fixture = native_fixture::Fixture::new();
    let mut sources = vec![];
    for (number, style, name) in [
        ("Q-12", "ABC123", "E\u{301}cole 中文"),
        ("Q-OTHER", "XYZ999", "OTHER"),
    ] {
        let mut invoice = InvoiceDraft::demo("2026-10-23", number).build().unwrap();
        invoice.shipment_date = "2026-10-24".into();
        invoice.items.truncate(1);
        invoice.items[0].style_no = style.into();
        invoice.items[0].style_name = name.into();
        let saved = fixture.create(CREATE_INVOICE, json!(invoice));
        sources
            .push(serde_json::from_value::<ApiInvoiceDetailDto>(saved["invoice"].clone()).unwrap());
    }
    for filter in [
        json!({}),
        json!({"keyword":"abc12"}),
        json!({"keyword":"école"}),
        json!({"keyword":"Q-1 中文"}),
        json!({"styleName":"éCOLE","startDate":"2026-10-01","endDateExclusive":"2026-11-01"}),
        json!({"contractNo":"absent"}),
        json!({"transportMode":"BY SEA"}),
        json!({"styleNo":"123"}),
    ] {
        let criteria = export_doc_domain::invoice_query::Criteria::new(
            serde_json::from_value(filter.clone()).unwrap(),
        )
        .unwrap();
        let mut expected = sources
            .iter()
            .filter(|row| criteria.matches(row).unwrap())
            .map(|row| row.id)
            .collect::<Vec<_>>();
        expected.sort();
        let parameters = filter
            .as_object()
            .unwrap()
            .iter()
            .map(|(key, value)| (key.as_str(), value.as_str().unwrap().to_owned()))
            .collect::<Vec<_>>();
        let response: Value = fixture
            .client()
            .json(LIST_QUERIED_INVOICES, &[], &parameters, None)
            .unwrap();
        export_doc_contracts::validation::response(LIST_QUERIED_INVOICES.id, &response).unwrap();
        let mut actual = response["items"]
            .as_array()
            .unwrap()
            .iter()
            .map(|row| row["id"].as_i64().unwrap())
            .collect::<Vec<_>>();
        actual.sort();
        assert_eq!(actual, expected, "filter={filter}");
    }
}
#[test]
fn dashboard_keeps_original_currencies_separate_without_an_exchange_rate() {
    use chrono::Datelike;
    let fixture = native_fixture::Fixture::new();
    let today = export_doc_engine::clock::BusinessClock::default()
        .now()
        .unwrap()
        .today;
    let previous = today
        .with_day(1)
        .unwrap()
        .checked_sub_months(chrono::Months::new(1))
        .unwrap();
    for (date, currency, number) in [
        (today, "USD", "USD-NOW"),
        (today, "CNY", "CNY-NOW"),
        (previous, "USD", "USD-BEFORE"),
    ] {
        let mut invoice = InvoiceDraft::demo(&date.to_string(), number)
            .build()
            .unwrap();
        invoice.currency = currency.into();
        invoice.exchange_rate = None;
        invoice.items.truncate(1);
        for item in &mut invoice.items {
            item.unit_price = 10.into();
            item.quantity = 10.into();
        }
        fixture.create(CREATE_INVOICE, json!(invoice));
    }
    let result = fixture.request(GET_DASHBOARD, None, None);
    export_doc_contracts::validation::response(GET_DASHBOARD.id, &result).unwrap();
    assert_eq!(result["monthlyExportAmount"], 0);
    assert_eq!(result["exportAmountCurrency"], "");
    let amounts = result["exportAmountsByCurrency"].as_array().unwrap();
    assert_eq!(amounts.len(), 2);
    let usd = amounts.iter().find(|row| row["currency"] == "USD").unwrap();
    let cny = amounts.iter().find(|row| row["currency"] == "CNY").unwrap();
    let amount = |row: &Value, key: &str| {
        serde_json::from_value::<rust_decimal::Decimal>(row[key].clone()).unwrap()
    };
    assert_eq!(amount(usd, "monthlyAmount"), 100.into());
    assert_eq!(amount(usd, "previousMonthlyAmount"), 100.into());
    assert_eq!(amount(cny, "monthlyAmount"), 100.into());
    assert_eq!(amount(cny, "previousMonthlyAmount"), 0.into());
}
