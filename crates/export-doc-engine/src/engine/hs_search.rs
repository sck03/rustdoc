use super::{
    error::{Result, invalid},
    hs::{self, codes_for_year, read},
    records::text,
};
use crate::engine::store::Connection;
use crate::operation;
use export_doc_domain::hs::{self as rules, Standard};
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub struct Resolution {
    pub code: String,
    pub status: &'static str,
    pub replacements: Vec<String>,
    pub usable: bool,
}
#[derive(Default)]
struct Evidence {
    sample: i64,
    count: usize,
    used: i64,
}
struct Candidate {
    sample: Option<i64>,
    standard: bool,
    score: i64,
    usable: bool,
    count: usize,
    used: i64,
}
pub fn resolve(
    raw: &str,
    current: &str,
    verified: bool,
    codes: &BTreeMap<String, Standard>,
    relations: &[Value],
) -> Resolution {
    let trusted = |code: &str| codes.get(code).is_some_and(Standard::trusted);
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
pub fn search(tx: &Connection, query: &[(&str, String)], year: i32) -> Result<Value> {
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
    let codes = codes_for_year(tx, year)?;
    let prefix = rules::code(raw).ok().filter(|c| c.len() >= 4);
    let fields = [
        "id",
        "rawReportedHsCode",
        "resolvedCurrentHsCode",
        "productName",
        "specification",
        "isManuallyVerified",
        "useCount",
    ]
    .map(|key| (key, key));
    let mut example_query = super::hs_queries::query(hs::EXAMPLES, &fields);
    example_query.prefix = prefix.as_deref().unwrap_or("");
    example_query.ranked_examples = true;
    let feedback = super::hs_queries::feedback(tx, &normalized)?;
    let mut relations = BTreeMap::<String, Vec<Value>>::new();
    let relation_fields = ["oldCode", "newCode", "isManuallyVerified"].map(|key| (key, key));
    super::hs_queries::visit(
        tx,
        &mut super::hs_queries::query(hs::REPLACEMENTS, &relation_fields),
        |row| {
            relations
                .entry(text(&row, "oldCode"))
                .or_default()
                .push(row);
            Ok(())
        },
    )?;
    let mut related_examples = BTreeMap::<String, Evidence>::new();
    let mut example_count = 0;
    let mut results = BTreeMap::<String, Candidate>::new();
    super::hs_queries::visit(tx, &mut example_query, |example| {
        example_count += 1;
        operation::check()?;
        let name = text(&example, "productName");
        let specification = text(&example, "specification");
        let raw_code = text(&example, "rawReportedHsCode");
        let current_code = text(&example, "resolvedCurrentHsCode");
        let sample = super::records::positive(&example, "id", "实例")?;
        let used = example["useCount"].as_i64().unwrap_or(0);
        for code in [&raw_code, &current_code]
            .into_iter()
            .enumerate()
            .filter(|(index, code)| {
                codes.get(*code).is_some_and(Standard::trusted)
                    && (*index == 0 || *code != &raw_code)
            })
            .map(|(_, code)| code)
        {
            let evidence = related_examples
                .entry(code.clone())
                .or_insert_with(|| Evidence {
                    sample,
                    ..Default::default()
                });
            evidence.count += 1;
            evidence.used = evidence.used.saturating_add(used);
        }
        if prefix.is_some() {
            return Ok(());
        }
        let combined = rules::text(&format!("{name} {specification}"));
        let assessment = rules::attributes(&normalized, &combined);
        let score = rules::score(&normalized, &combined).max(
            ((rules::score(&normalized, &rules::text(&name)) * 72
                + rules::score(&normalized, &rules::text(&specification)) * 28) as f64
                / 100.)
                .round_ties_even() as i64,
        ) - assessment.penalty;
        if score < 18 {
            return Ok(());
        }
        let resolved = resolve(
            &raw_code,
            &current_code,
            example["isManuallyVerified"] == true,
            &codes,
            relations
                .get(&raw_code)
                .map(Vec::as_slice)
                .unwrap_or_default(),
        );
        let candidate = if resolved.code.is_empty() {
            raw_code.clone()
        } else {
            resolved.code.clone()
        };
        let boost = feedback.get(&candidate).copied().unwrap_or(0);
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
        let previous = results.entry(key).or_insert(Candidate {
            sample: Some(sample),
            standard: false,
            score,
            usable: resolved.usable,
            count: 0,
            used: 0,
        });
        previous.count += 1;
        previous.used = previous.used.saturating_add(used);
        if score > previous.score {
            previous.sample = Some(sample);
            previous.score = score;
            previous.usable = resolved.usable;
        }
        Ok(())
    })?;
    for (code, standard) in &codes {
        operation::check()?;
        if !standard.trusted() || results.contains_key(code) {
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
        let related = related_examples.get(code);
        results.insert(
            code.clone(),
            Candidate {
                sample: related.map(|r| r.sample),
                standard: true,
                score,
                usable: true,
                count: related.map_or(0, |r| r.count),
                used: related.map_or(0, |r| r.used),
            },
        );
    }
    // Rank only compact identities/counters. Hydrate at most the final result limit.
    let mut rows: Vec<_> = results.into_iter().collect();
    rows.sort_by(|a, b| {
        (!a.1.usable)
            .cmp(&(!b.1.usable))
            .then_with(|| b.1.score.cmp(&a.1.score))
            .then_with(|| {
                fn code(key: &str) -> &str {
                    if key.starts_with("obsolete:") {
                        ""
                    } else {
                        key
                    }
                }
                code(&a.0).cmp(code(&b.0))
            })
    });
    rows.truncate(limit);
    let ids: Vec<_> = rows
        .iter()
        .filter_map(|(_, candidate)| candidate.sample)
        .collect::<std::collections::BTreeSet<_>>()
        .into_iter()
        .collect();
    let samples: BTreeMap<_, _> = tx
        .records_by_ids(hs::EXAMPLES, &ids, &fields)?
        .into_iter()
        .map(|row| Ok((super::records::positive(&row, "id", "实例")?, row)))
        .collect::<Result<_>>()?;
    let rows = rows
        .into_iter()
        .map(|(code, candidate)| {
            render_candidate(
                &code,
                candidate,
                &samples,
                &codes,
                &relations,
                &normalized,
                prefix.as_deref(),
            )
        })
        .collect::<Result<Vec<_>>>()?;
    let message = if rows.is_empty() {
        "没有匹配的本地知识，请补充商品属性或维护经过验证的税则。"
    } else {
        "候选按属性及本地证据排序，请结合申报要素确认。"
    };
    Ok(json!({"query":raw,"items":rows,"localExampleCount":example_count,"message":message}))
}
fn render_candidate(
    code: &str,
    candidate: Candidate,
    samples: &BTreeMap<i64, Value>,
    codes: &BTreeMap<String, Standard>,
    relations: &BTreeMap<String, Vec<Value>>,
    normalized: &str,
    prefix: Option<&str>,
) -> Result<Value> {
    let sample = candidate
        .sample
        .map(|id| {
            samples
                .get(&id)
                .ok_or_else(|| super::error::unavailable("查询快照中的 HS 实例缺失。"))
        })
        .transpose()?;
    let (raw, name, specification, resolved, combined) = if candidate.standard {
        let standard = &codes[code];
        let specification = sample
            .map(|r| text(r, "specification"))
            .filter(|s| !s.is_empty())
            .unwrap_or_else(|| {
                if standard.elements.is_empty() {
                    standard.description.clone()
                } else {
                    standard.elements.clone()
                }
            });
        (
            code.to_string(),
            sample
                .map(|r| text(r, "productName"))
                .unwrap_or_else(|| standard.name.clone()),
            specification,
            Resolution {
                code: code.into(),
                status: "Active",
                replacements: vec![],
                usable: true,
            },
            rules::text(&format!(
                "{} {} {}",
                standard.name, standard.elements, standard.description
            )),
        )
    } else {
        let sample = sample.ok_or_else(|| super::error::unavailable("HS 实例候选缺少来源。"))?;
        let raw = text(sample, "rawReportedHsCode");
        let name = text(sample, "productName");
        let specification = text(sample, "specification");
        let resolved = resolve(
            &raw,
            &text(sample, "resolvedCurrentHsCode"),
            sample["isManuallyVerified"] == true,
            codes,
            relations.get(&raw).map(Vec::as_slice).unwrap_or_default(),
        );
        let combined = rules::text(&format!("{name} {specification}"));
        (raw, name, specification, resolved, combined)
    };
    let assessment = rules::attributes(normalized, &combined);
    let reasons = if candidate.standard {
        prefix
            .map(|p| vec![format!("HS 编码前缀匹配：{p}")])
            .unwrap_or(assessment.reasons)
    } else {
        assessment.reasons
    };
    let mut row = result(
        &raw,
        &name,
        &specification,
        &resolved,
        codes.get(&resolved.code),
        candidate.score,
        reasons,
        assessment.warnings,
    );
    row["exampleCount"] = json!(candidate.count);
    row["confirmedCount"] = json!(candidate.used);
    Ok(row)
}
fn result(
    raw: &str,
    name: &str,
    spec: &str,
    resolution: &Resolution,
    standard: Option<&Standard>,
    score: i64,
    reasons: Vec<String>,
    warnings: Vec<String>,
) -> Value {
    json!({"currentCode":resolution.code,"rawCode":raw,"name":name,"specification":spec,"standardName":standard.map(|s|s.name.as_str()).unwrap_or(""),"resolutionStatus":resolution.status,"score":score,"exampleCount":0,"confirmedCount":0,"replacementCandidates":resolution.replacements,"matchReasons":reasons,"conflictWarnings":warnings,"standardSource":standard.map(|s|s.source_name.as_str()).unwrap_or(""),"effectiveYear":standard.and_then(|s|s.effective_year),"lastVerifiedAt":standard.and_then(|s|s.last_verified_at.as_deref()),"canUse":resolution.usable&&standard.is_some_and(Standard::trusted)})
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn low_usage_examples_beyond_the_old_prefix_participate_in_matching() {
        let scratch = crate::engine::test_support::ScratchDirectory::new();
        let service = scratch.service();
        let actor = crate::engine::test_support::administrator();
        service.store.transaction(|tx| {
            crate::engine::store::save(tx, hs::CODES, 0, json!({"code":"3917320000","name":"其他塑料制品",
                "status":"Active","sourceName":"测试年度税则","effectiveYear":2026,"lastVerifiedAt":"2026-11-19T00:00:00Z",
                "elements":"","description":""}), Some("3917320000".into()), &actor, "create")?;
            for index in 0..2101 {
                crate::engine::store::save(tx, hs::EXAMPLES, 0, json!({"rawReportedHsCode":"3917320000",
                    "resolvedCurrentHsCode":"3917320000","productName":if index == 2100 {"实验用硅胶导管"} else {"普通包装塑料薄膜"},
                    "specification":"","isManuallyVerified":true,"useCount":if index == 2100 {0} else {1}}),
                    Some(format!("example-{index}")), &actor, "create")?;
            }
            Ok(())
        }).unwrap();
        let response = service
            .store
            .read(|tx| {
                search(
                    tx,
                    &[
                        ("query", "实验用硅胶导管".into()),
                        ("maxResults", "1".into()),
                    ],
                    2026,
                )
            })
            .unwrap();
        assert_eq!(response["localExampleCount"], 2101);
        assert_eq!(response["items"].as_array().unwrap().len(), 1);
        assert_eq!(response["items"][0]["name"], "实验用硅胶导管");
    }
}
