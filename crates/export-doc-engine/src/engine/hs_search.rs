use super::{
    error::{Result, invalid},
    hs::{self, codes, read},
    records::text,
    store,
};
use crate::operation;
use export_doc_domain::{generated_api::ApiHsCodeDto, hs as rules};
use export_doc_storage::Connection;
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub struct Resolution {
    pub code: String,
    pub status: &'static str,
    pub replacements: Vec<String>,
    pub usable: bool,
}
pub fn resolve(
    raw: &str,
    current: &str,
    verified: bool,
    codes: &BTreeMap<String, ApiHsCodeDto>,
    relations: &[Value],
) -> Resolution {
    let trusted = |code: &str| codes.get(code).is_some_and(rules::trusted);
    let make = |code: String, status, replacements, usable| Resolution {
        code,
        status,
        replacements,
        usable,
    };
    if !current.is_empty() && trusted(current) {
        return make(
            current.into(),
            if verified {
                "ManuallyVerified"
            } else {
                "SuggestedReplacement"
            },
            vec![],
            verified,
        );
    }
    if trusted(raw) {
        return make(raw.into(), "Active", vec![], true);
    }
    let replacements: Vec<String> = relations
        .iter()
        .filter(|r| r["oldCode"] == raw)
        .map(|r| text(r, "newCode"))
        .filter(|c| trusted(c))
        .collect::<std::collections::BTreeSet<_>>()
        .into_iter()
        .collect();
    let verified: Vec<_> = relations
        .iter()
        .filter(|r| r["oldCode"] == raw && r["isManuallyVerified"] == true)
        .map(|r| text(r, "newCode"))
        .filter(|c| trusted(c))
        .collect::<std::collections::BTreeSet<_>>()
        .into_iter()
        .collect();
    if verified.len() == 1 {
        return make(verified[0].clone(), "ObsoleteMapped", verified, true);
    }
    if replacements.len() == 1 {
        return make(
            replacements[0].clone(),
            "SuggestedReplacement",
            replacements,
            false,
        );
    }
    make(
        String::new(),
        if replacements.len() > 1 {
            "Ambiguous"
        } else {
            "ObsoleteUnresolved"
        },
        replacements,
        false,
    )
}
pub fn search(tx: &Connection, query: &[(&str, String)]) -> Result<Value> {
    let raw = read(query, "query").trim();
    if raw.chars().count() > 500 {
        return Err(invalid("查询条件不能超过 500 字。"));
    }
    if raw.is_empty() {
        return Ok(
            json!({"query":"","items":[],"localExampleCount":0,"message":"请输入商品名称、材质、用途、规格或至少 4 位 HS 编码。"}),
        );
    }
    let normalized = rules::text(raw);
    let limit = read(query, "maxResults")
        .parse::<usize>()
        .unwrap_or(20)
        .clamp(1, 50);
    let codes = codes(tx)?;
    let relations = store::all(tx, hs::REPLACEMENTS)?;
    let feedback = store::all(tx, hs::FEEDBACK)?;
    let mut examples = store::all(tx, hs::EXAMPLES)?;
    examples.sort_by(|a, b| {
        (a["isManuallyVerified"] != true)
            .cmp(&(b["isManuallyVerified"] != true))
            .then_with(|| b["useCount"].as_i64().cmp(&a["useCount"].as_i64()))
            .then_with(|| b["updatedAt"].as_str().cmp(&a["updatedAt"].as_str()))
    });
    let prefix = rules::code(raw).ok().filter(|c| c.len() >= 4);
    if let Some(prefix) = &prefix {
        examples.retain(|r| {
            text(r, "rawReportedHsCode").starts_with(prefix)
                || text(r, "resolvedCurrentHsCode").starts_with(prefix)
        });
    }
    examples.truncate(2000);
    let mut results: BTreeMap<String, Value> = BTreeMap::new();
    if prefix.is_none() {
        for example in &examples {
            operation::check()?;
            let name = text(example, "productName");
            let specification = text(example, "specification");
            let combined = rules::text(&format!("{name} {specification}"));
            let assessment = rules::attributes(&normalized, &combined);
            let score = rules::score(&normalized, &combined).max(
                ((rules::score(&normalized, &rules::text(&name)) * 72
                    + rules::score(&normalized, &rules::text(&specification)) * 28)
                    as f64
                    / 100.)
                    .round_ties_even() as i64,
            ) - assessment.penalty;
            if score < 18 {
                continue;
            }
            let raw_code = text(example, "rawReportedHsCode");
            let resolved = resolve(
                &raw_code,
                &text(example, "resolvedCurrentHsCode"),
                example["isManuallyVerified"] == true,
                &codes,
                &relations,
            );
            let candidate = if resolved.code.is_empty() {
                raw_code.clone()
            } else {
                resolved.code.clone()
            };
            let boost: i64 = feedback
                .iter()
                .filter(|r| {
                    rules::text(&text(r, "queryText")) == normalized
                        && r["candidateCode"] == candidate
                })
                .map(|r| {
                    r["acceptedCount"].as_i64().unwrap_or(0).saturating_mul(5)
                        - r["rejectedCount"].as_i64().unwrap_or(0).saturating_mul(4)
                })
                .fold(0, i64::saturating_add);
            let used = example["useCount"].as_i64().unwrap_or(0);
            let score = (score
                + used.saturating_mul(2).min(15)
                + if example["isManuallyVerified"] == true {
                    15
                } else {
                    0
                })
            .saturating_add(boost)
            .clamp(0, 100);
            let key = if resolved.code.is_empty() {
                format!("obsolete:{raw_code}")
            } else {
                resolved.code.clone()
            };
            let standard = codes.get(&resolved.code);
            let mut row = result(
                &raw_code,
                &name,
                &specification,
                &resolved,
                standard,
                score,
                assessment.reasons,
                assessment.warnings,
            );
            row["exampleCount"] = json!(1);
            row["confirmedCount"] = json!(used);
            if let Some(previous) = results.get_mut(&key) {
                let count = previous["exampleCount"].as_i64().unwrap_or(0) + 1;
                let confirmed = previous["confirmedCount"]
                    .as_i64()
                    .unwrap_or(0)
                    .saturating_add(used);
                if score > previous["score"].as_i64().unwrap_or(0) {
                    *previous = row;
                }
                previous["exampleCount"] = json!(count);
                previous["confirmedCount"] = json!(confirmed);
            } else {
                results.insert(key, row);
            }
        }
    }
    for (code, standard) in &codes {
        operation::check()?;
        if !rules::trusted(standard) || results.contains_key(code) {
            continue;
        }
        let combined = rules::text(&format!(
            "{} {} {}",
            standard.name, standard.elements, standard.description
        ));
        let assessment = rules::attributes(&normalized, &combined);
        let score = if let Some(prefix) = &prefix {
            if !code.starts_with(prefix) {
                continue;
            }
            if code == prefix {
                100
            } else {
                (72 + prefix.len() as i64 * 3).min(96)
            }
        } else {
            rules::score(&normalized, &combined) - assessment.penalty
        };
        if score < 22 {
            continue;
        }
        let related: Vec<_> = examples
            .iter()
            .filter(|r| r["rawReportedHsCode"] == *code || r["resolvedCurrentHsCode"] == *code)
            .collect();
        let name = related
            .first()
            .map(|r| text(r, "productName"))
            .unwrap_or_else(|| standard.name.clone());
        let specification = related
            .first()
            .map(|r| text(r, "specification"))
            .filter(|s| !s.is_empty())
            .unwrap_or_else(|| {
                if !standard.elements.is_empty() {
                    standard.elements.clone()
                } else {
                    standard.description.clone()
                }
            });
        let resolution = Resolution {
            code: code.clone(),
            status: "Active",
            replacements: vec![],
            usable: true,
        };
        let reasons = if let Some(prefix) = &prefix {
            vec![format!("HS 编码前缀匹配：{prefix}")]
        } else {
            assessment.reasons
        };
        let mut row = result(
            code,
            &name,
            &specification,
            &resolution,
            Some(standard),
            score,
            reasons,
            assessment.warnings,
        );
        row["exampleCount"] = json!(related.len());
        row["confirmedCount"] = json!(
            related
                .iter()
                .filter_map(|r| r["useCount"].as_i64())
                .sum::<i64>()
        );
        results.insert(code.clone(), row);
    }
    let mut rows: Vec<_> = results.into_values().collect();
    rows.sort_by(|a, b| {
        (a["canUse"] != true)
            .cmp(&(b["canUse"] != true))
            .then_with(|| b["score"].as_i64().cmp(&a["score"].as_i64()))
            .then_with(|| a["currentCode"].as_str().cmp(&b["currentCode"].as_str()))
    });
    rows.truncate(limit);
    let message = if rows.is_empty() {
        "没有匹配的本地知识，请补充商品属性或维护经过验证的税则。"
    } else {
        "候选按属性及本地证据排序，请结合申报要素确认。"
    };
    Ok(json!({"query":raw,"items":rows,"localExampleCount":examples.len(),"message":message}))
}
fn result(
    raw: &str,
    name: &str,
    spec: &str,
    resolution: &Resolution,
    standard: Option<&ApiHsCodeDto>,
    score: i64,
    reasons: Vec<String>,
    warnings: Vec<String>,
) -> Value {
    json!({"currentCode":resolution.code,"rawCode":raw,"name":name,"specification":spec,"standardName":standard.map(|s|s.name.as_str()).unwrap_or(""),"resolutionStatus":resolution.status,"score":score,"exampleCount":0,"confirmedCount":0,"replacementCandidates":resolution.replacements,"matchReasons":reasons,"conflictWarnings":warnings,"standardSource":standard.map(|s|s.source_name.as_str()).unwrap_or(""),"effectiveYear":standard.and_then(|s|s.effective_year),"lastVerifiedAt":standard.and_then(|s|s.last_verified_at.as_deref()),"canUse":resolution.usable&&standard.is_some_and(rules::trusted)})
}
