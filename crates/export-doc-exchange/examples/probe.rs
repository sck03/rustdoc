fn main() -> Result<(), Box<dyn std::error::Error>> {
    let service = export_doc_exchange::ExchangeRates::default();
    let source = std::env::args()
        .nth(1)
        .unwrap_or_else(|| "BankOfChinaHtml".into());
    let url = match source.as_str() {
        "Icbc" => "https://www.icbc.com.cn/column/1438058341489590354.html",
        "Cmb" => "https://fx.cmbchina.com/hq/",
        _ => export_doc_exchange::DEFAULT_URL,
    };
    let configuration = export_doc_exchange::Configuration::read(
        &serde_json::json!({"url":url,"sourceFormat":source,"selectedCurrencies":["美元","欧元","日元"],"cacheDurationMinutes":30}),
    )?;
    let quotation = service.get(&configuration, true, false, chrono::Utc::now(), &|| Ok(()))?;
    println!(
        "{}",
        serde_json::json!({"source":configuration.source.url,"fetchedAt":quotation.fetched_at,"rates":quotation.rates})
    );
    Ok(())
}
