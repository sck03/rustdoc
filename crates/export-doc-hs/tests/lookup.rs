use export_doc_contracts::generated_api::ApiHsCodeDto;
use export_doc_hs::{
    lookup::{self, Source},
    parser::{self, DetailBundle, RemoteRecordKind, SearchBundle},
};
use std::collections::BTreeMap;

const OBSERVED: &str = "2026-09-21T00:00:00Z";

struct FixtureSource {
    pages: BTreeMap<String, String>,
    calls: Vec<String>,
}
impl Source for FixtureSource {
    fn search(&mut self, keyword: &str) -> Result<SearchBundle, String> {
        self.calls.push(format!("search:{keyword}"));
        parser::search(self.pages.get(keyword).expect("unexpected query"), OBSERVED)
    }
    fn detail(&mut self, seed: &ApiHsCodeDto) -> Result<DetailBundle, String> {
        let key = format!("detail:{}", seed.code);
        self.calls.push(key.clone());
        parser::detail(
            self.pages.get(&key).expect("unexpected detail lookup"),
            seed,
            OBSERVED,
        )
    }
}

#[test]
fn tshirts_keep_current_source_order_and_do_not_follow_obsolete_recommendations() {
    let mut source = FixtureSource {
        pages: BTreeMap::from([("T恤衫".into(), include_str!("fixtures/tshirts.html").into())]),
        calls: vec![],
    };
    let bundle = lookup::search("T恤衫", &mut source, &|| Ok(())).unwrap();
    assert_eq!(source.calls, ["search:T恤衫"]);
    assert_eq!(
        bundle
            .records
            .iter()
            .filter(|r| !r.expired)
            .map(|r| r.item.code.as_str())
            .collect::<Vec<_>>(),
        ["6109100000", "6109909000", "6109901000"]
    );
}

#[test]
fn mens_tshirts_resolve_all_three_materials_and_preserve_fifteen_original_examples() {
    let mut source = FixtureSource {
        pages: BTreeMap::from([
            (
                "男T恤衫".into(),
                include_str!("fixtures/mens-tshirts.html").into(),
            ),
            (
                "61091000".into(),
                include_str!("fixtures/key-61091000.html").into(),
            ),
            (
                "61099010".into(),
                include_str!("fixtures/key-61099010.html").into(),
            ),
            (
                "61099090".into(),
                include_str!("fixtures/key-61099090.html").into(),
            ),
            (
                "610910".into(),
                include_str!("fixtures/key-610910.html").into(),
            ),
            (
                "610990".into(),
                include_str!("fixtures/key-610990.html").into(),
            ),
            (
                "detail:6109100010".into(),
                include_str!("fixtures/detail-6109100010.html").into(),
            ),
            (
                "detail:6109100021".into(),
                include_str!("fixtures/detail-6109100021.html").into(),
            ),
            (
                "detail:6109901011".into(),
                include_str!("fixtures/detail-6109901011.html").into(),
            ),
            (
                "detail:6109909050".into(),
                include_str!("fixtures/detail-6109909050.html").into(),
            ),
        ]),
        calls: vec![],
    };
    let bundle = lookup::search("男T恤衫", &mut source, &|| Ok(())).unwrap();
    let standards = bundle
        .records
        .iter()
        .filter(|r| r.kind == RemoteRecordKind::StandardCode && !r.expired)
        .collect::<Vec<_>>();
    assert_eq!(
        standards
            .iter()
            .map(|r| r.item.code.as_str())
            .collect::<Vec<_>>(),
        ["6109100000", "6109901000", "6109909000"]
    );
    let examples = bundle
        .records
        .iter()
        .filter(|r| r.kind == RemoteRecordKind::DeclarationExample)
        .collect::<Vec<_>>();
    assert_eq!(examples.len(), 15);
    assert_eq!(examples[0].item.name, "棉制针织男T恤衫");
    assert!(examples[0].item.description.contains("100%棉"));
    assert_eq!(
        source
            .calls
            .iter()
            .filter(|v| v.starts_with("detail:"))
            .count(),
        4
    );
    for code in ["6109100010", "6109100021", "6109901011", "6109909050"] {
        assert!(
            bundle
                .replacements
                .iter()
                .any(|r| r.old_code == code && !r.recommended_keywords.is_empty())
        );
    }
}

#[test]
fn real_detail_keeps_tax_fields_references_and_examples_inside_wrappers() {
    let seed = ApiHsCodeDto {
        code: "6109100021".into(),
        detail_url: "https://www.i5a6.com/hscode/detail/6109100021".into(),
        ..Default::default()
    };
    let detail = parser::detail(
        include_str!("fixtures/detail-6109100021.html"),
        &seed,
        OBSERVED,
    )
    .unwrap();
    assert!(detail.expired);
    assert_eq!(detail.recommended_keywords, ["61091000", "610910"]);
    assert!(!detail.item.elements.is_empty());
    assert!(!detail.item.unit.is_empty());
    assert!(!detail.item.rebate_rate.is_empty());
    assert_eq!(detail.personal_postal_tax_code, "04010400");
    assert_eq!(detail.ciq_entries.len(), 1);
    assert_eq!(detail.ciq_entries[0].code, "6109100021.999");
    assert_eq!(detail.classification_entries.len(), 4);
    assert_eq!(detail.declaration_examples.len(), 20);
}

#[test]
fn mens_trousers_matches_original_standard_results_without_promoting_examples() {
    let mut source = FixtureSource {
        pages: BTreeMap::from([(
            "男裤".into(),
            include_str!("fixtures/mens-trousers.html").into(),
        )]),
        calls: vec![],
    };
    let bundle = lookup::search("男裤", &mut source, &|| Ok(())).unwrap();
    assert_eq!(source.calls, ["search:男裤"]);
    let standards = bundle
        .records
        .iter()
        .filter(|r| r.kind == RemoteRecordKind::StandardCode && !r.expired)
        .collect::<Vec<_>>();
    assert_eq!(
        standards
            .iter()
            .map(|r| r.item.code.as_str())
            .collect::<Vec<_>>(),
        ["6103420090", "6103430090"]
    );
    assert_eq!(standards[0].instance_count, Some(966));
    assert!(!standards[0].item.description.is_empty());
    assert!(standards[0].item.elements.is_empty());
    assert_eq!(
        bundle
            .records
            .iter()
            .filter(|r| r.kind == RemoteRecordKind::DeclarationExample)
            .count(),
        15
    );
    assert_eq!(bundle.records.iter().filter(|r| r.expired).count(), 7);
}

#[test]
fn recommendations_visit_all_branches_without_importing_nested_examples() {
    let old = |code: &str, next: &str| {
        format!(
            "<table><tr><th>HS编码</th><th>商品名称</th><th>申报实例</th></tr><tr><td>{code} 已作废 <a href='/hscode/key/{next}'>推荐查询: {next}</a></td><td>历史商品</td><td>0条</td></tr></table>"
        )
    };
    let current = |code: &str| {
        format!(
            "<table><tr><th>HS编码</th><th>商品名称</th><th>申报实例</th></tr><tr><td>{code}</td><td>现行商品</td><td>0条</td></tr></table>"
        )
    };
    let mut source = FixtureSource {
        pages: BTreeMap::from([
            (
                "历史查询".into(),
                format!(
                    "<table><tr><th>HS编码</th><th>商品名称</th><th>申报实例</th></tr><tr><td>6103410090 已作废 <a href='/hscode/key/61034100'>推荐查询: 61034100</a><a href='/hscode/key/62034390'>或者: 62034390</a></td><td>男裤</td><td>0条</td></tr></table>"
                ),
            ),
            ("61034100".into(), old("6103410090", "610341")),
            ("610341".into(), current("6103410000")),
            ("62034390".into(), current("6203439000")),
        ]),
        calls: vec![],
    };
    let bundle = lookup::search("历史查询", &mut source, &|| Ok(())).unwrap();
    let codes = bundle
        .records
        .iter()
        .filter(|r| !r.expired)
        .map(|r| r.item.code.as_str())
        .collect::<Vec<_>>();
    assert_eq!(codes, ["6103410000", "6203439000"]);
}
