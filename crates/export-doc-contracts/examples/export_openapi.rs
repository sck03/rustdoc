fn main() -> Result<(), Box<dyn std::error::Error>> {
    let destination = std::env::args().nth(1).ok_or("provide output path")?;
    std::fs::write(
        destination,
        serde_json::to_string_pretty(&export_doc_contracts::openapi::document())? + "\n",
    )?;
    Ok(())
}
