//! Evidence traversal shared by hosts. No persistence, HTTP DTO projection or
//! knowledge-candidate mutation belongs in this module.
use crate::parser::{
    DetailBundle, RemoteRecord, RemoteRecordKind, ReplacementEvidence, SearchBundle,
};
use export_doc_contracts::generated_api::ApiHsCodeDto;
use export_doc_domain::hs;
use std::collections::BTreeSet;

const MAX_DETAIL_LOOKUPS: usize = 12;
const MAX_RECOMMENDATION_DEPTH: usize = 3;

pub trait Source {
    fn search(&mut self, keyword: &str) -> Result<SearchBundle, String>;
    fn detail(&mut self, item: &ApiHsCodeDto) -> Result<DetailBundle, String>;
}

pub fn search(
    keyword: &str,
    source: &mut impl Source,
    check: &dyn Fn() -> Result<(), String>,
) -> Result<SearchBundle, String> {
    check()?;
    let initial = source.search(keyword)?;
    check()?;
    // Original semantics: valid standard results already answer the query.
    // Do not turn unrelated historical examples into additional current codes.
    if has_current_standard(&initial.records) {
        return Ok(initial);
    }
    let mut traversal = Traversal {
        source,
        check,
        result: SearchBundle {
            records: initial.records.clone(),
            replacements: Vec::new(),
        },
        queries: BTreeSet::from([identity(keyword)]),
        details: BTreeSet::new(),
        remaining_details: MAX_DETAIL_LOOKUPS,
    };
    traversal.resolve(initial, 0)?;
    let mut result = traversal.result;
    let mut seen = BTreeSet::new();
    result.records.retain(|record| {
        seen.insert((
            record.kind.as_str(),
            identity(&record.item.code),
            record.item.name.trim().to_lowercase(),
            record.item.description.trim().to_lowercase(),
        ))
    });
    let mut replacements: Vec<ReplacementEvidence> = Vec::new();
    for evidence in result.replacements {
        if let Some(previous) = replacements
            .iter_mut()
            .find(|v| identity(&v.old_code) == identity(&evidence.old_code))
        {
            previous
                .recommended_keywords
                .extend(evidence.recommended_keywords);
        } else {
            replacements.push(evidence);
        }
    }
    for evidence in &mut replacements {
        let mut seen = BTreeSet::new();
        evidence
            .recommended_keywords
            .retain(|v| !v.trim().is_empty() && seen.insert(identity(v)));
    }
    result.replacements = replacements;
    Ok(result)
}

struct Traversal<'a, S> {
    source: &'a mut S,
    check: &'a dyn Fn() -> Result<(), String>,
    result: SearchBundle,
    queries: BTreeSet<String>,
    details: BTreeSet<String>,
    remaining_details: usize,
}

impl<S: Source> Traversal<'_, S> {
    fn resolve(&mut self, bundle: SearchBundle, depth: usize) -> Result<(), String> {
        (self.check)()?;
        self.result.records.extend(
            bundle
                .records
                .iter()
                .filter(|r| r.kind == RemoteRecordKind::StandardCode)
                .cloned(),
        );
        self.result
            .replacements
            .extend(bundle.replacements.iter().cloned());
        if has_current_standard(&bundle.records) {
            return Ok(());
        }
        let mut recommendations: Vec<String> = bundle
            .replacements
            .iter()
            .flat_map(|r| r.recommended_keywords.iter().cloned())
            .collect();
        for record in bundle.records.iter().filter(|r| {
            r.kind == RemoteRecordKind::DeclarationExample && !r.item.detail_url.is_empty()
        }) {
            (self.check)()?;
            if self.remaining_details == 0 {
                break;
            }
            if !self.details.insert(identity(&record.item.code)) {
                continue;
            }
            self.remaining_details -= 1;
            let detail = self.source.detail(&record.item);
            // An optional enrichment failure may leave the original evidence;
            // cancellation must still propagate instead of becoming success.
            (self.check)()?;
            let Ok(detail) = detail else {
                continue;
            };
            if !detail.expired {
                self.result.records.push(RemoteRecord {
                    item: detail.item,
                    kind: RemoteRecordKind::StandardCode,
                    expired: false,
                    instance_count: detail.instance_count,
                    summary_url: record.summary_url.clone(),
                    evidence_url: detail.evidence_url.clone(),
                });
            }
            if !detail.recommended_keywords.is_empty() {
                recommendations.extend(detail.recommended_keywords.iter().cloned());
                self.result.replacements.push(ReplacementEvidence {
                    old_code: record.item.normalized_code.clone(),
                    recommended_keywords: detail.recommended_keywords,
                    evidence_url: detail.evidence_url,
                });
            }
        }
        if depth >= MAX_RECOMMENDATION_DEPTH {
            return Ok(());
        }
        for keyword in recommendations {
            (self.check)()?;
            if !self.queries.insert(identity(&keyword)) {
                continue;
            }
            let nested = self.source.search(&keyword);
            (self.check)()?;
            if let Ok(nested) = nested {
                self.resolve(nested, depth + 1)?;
            }
        }
        Ok(())
    }
}

fn identity(value: &str) -> String {
    hs::code(value)
        .unwrap_or_else(|_| hs::text(value))
        .to_lowercase()
}
fn has_current_standard(records: &[RemoteRecord]) -> bool {
    records
        .iter()
        .any(|r| r.kind == RemoteRecordKind::StandardCode && !r.expired)
}
