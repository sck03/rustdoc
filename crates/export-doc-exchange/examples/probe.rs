fn main() -> Result<(), Box<dyn std::error::Error>> {
    let service = export_doc_exchange::ExchangeRates::default();
    let quotation = service.get(
        &serde_json::json!({"selectedCurrencies":["美元","欧元","日元"],"cacheDurationMinutes":0}),
        true,
        false,
        chrono::Utc::now(),
        &|| Ok(()),
    )?;
    println!(
        "{}",
        serde_json::json!({"source":quotation.source,"fetchedAt":quotation.fetched_at,"rates":quotation.rates})
    );
    Ok(())
}
