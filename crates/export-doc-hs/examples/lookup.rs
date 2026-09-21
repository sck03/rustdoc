//! Explicit live probe: pass the keyword and actual RFC 3339 observation time.
use export_doc_contracts::generated_api::ApiHsCodeDto;
use export_doc_hs::{
    lookup::{self, Source},
    parser::{DetailBundle, RemoteRecordKind, SearchBundle},
};
struct Live {
    observed: String,
}
impl Source for Live {
    fn search(&mut self, keyword: &str) -> Result<SearchBundle, String> {
        export_doc_hs::search(keyword, &self.observed, &|| Ok(()))
    }
    fn detail(&mut self, seed: &ApiHsCodeDto) -> Result<DetailBundle, String> {
        export_doc_hs::detail(seed, &self.observed, &|| Ok(()))
    }
}
fn main() -> Result<(), String> {
    let keyword = std::env::args().nth(1).ok_or("provide a search keyword")?;
    let observed = std::env::args()
        .nth(2)
        .ok_or("provide the actual RFC 3339 observation time")?;
    let bundle = lookup::search(&keyword, &mut Live { observed }, &|| Ok(()))?;
    for record in bundle
        .records
        .iter()
        .filter(|r| r.kind == RemoteRecordKind::StandardCode && !r.expired)
    {
        println!(
            "{}\t{}\t{}\t{:?}",
            record.item.code, record.item.name, record.item.description, record.instance_count
        );
    }
    println!(
        "examples={}",
        bundle
            .records
            .iter()
            .filter(|r| r.kind == RemoteRecordKind::DeclarationExample)
            .count()
    );
    Ok(())
}
