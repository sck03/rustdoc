//! Anonymous local protocol probes; this command never sends files.
#[path = "../tests/support/declarations.rs"]
mod declarations;
use export_doc_domain::single_window::{Business, validation, xml};
fn main() {
    let directory = std::env::args_os()
        .nth(1)
        .expect("output directory required");
    let directory = std::path::Path::new(&directory);
    assert!(
        directory.is_absolute(),
        "use an explicit absolute output directory"
    );
    std::fs::create_dir_all(directory).unwrap();
    let mut samples = vec![("ACD".to_owned(), Business::Acd, declarations::acd())];
    samples.extend(
        declarations::COMMON
            .into_iter()
            .map(|(cert, country, sequence)| {
                (
                    format!("COO-{cert}"),
                    Business::Coo,
                    declarations::coo(cert, country, sequence),
                )
            }),
    );
    for (name, business, document) in samples {
        let issues = validation::review(business, &document);
        assert!(issues.is_empty(), "{name}: {issues:?}");
        let path = directory.join(format!("SW-TEST-20261109-{name}.xml"));
        std::fs::write(&path, xml::payload(business, &document).unwrap()).unwrap();
        println!("{}", path.display());
    }
}
