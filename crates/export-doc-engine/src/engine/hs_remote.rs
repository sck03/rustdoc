use super::{
    NativeService,
    error::{Result, invalid, unavailable},
    hs, hs_search,
    records::text,
    store::{self, Actor},
};
use crate::{generated_api::*, operation};
use export_doc_domain::hs as rules;
use export_doc_hs::parser::{RemoteRecord, RemoteRecordKind, SearchBundle};
use serde_json::{Value, json};
use std::collections::{BTreeMap, BTreeSet};

pub const OPERATIONS: &[Operation] = &[
    SEARCH_REMOTE_HS_CODES,
    CAPTURE_REMOTE_HS_CODES,
    FETCH_REMOTE_HS_CODE_DETAIL,
    RESOLVE_REMOTE_HS_CODE_DETAIL,
    GET_HS_CODE_REMOTE_HEALTH,
];
const MAX_DETAIL_LOOKUPS: usize = 12;
const MAX_RECOMMENDATION_DEPTH: usize = 3;

fn check() -> std::result::Result<(), String> {
    operation::check().map_err(|error| error.to_string())
}

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let observed = store::timestamp();
    if operation == GET_HS_CODE_REMOTE_HEALTH {
        let health = export_doc_hs::health(&check);
        operation::check()?;
        return Ok(json!({
            "source": export_doc_hs::SOURCE,
            "available": health.is_ok(),
            "checkedAt": observed,
            "message": health.err().unwrap_or_else(|| "静态参考页面可访问。".into())
        }));
    }
    if [SEARCH_REMOTE_HS_CODES, CAPTURE_REMOTE_HS_CODES].contains(&operation) {
        let keyword = if operation == SEARCH_REMOTE_HS_CODES {
            hs::read(query, "keyword").to_string()
        } else {
            text(body, "keyword")
        };
        if keyword.trim().is_empty() || keyword.chars().count() > 500 {
            return Err(invalid("请输入 1 至 500 字的检索条件。"));
        }
        let bundle = search_bundle(&keyword, &observed)?;
        operation::check()?;
        if operation == CAPTURE_REMOTE_HS_CODES {
            capture(service, actor, &keyword, &bundle)?;
        }
        return Ok(search_response(
            &bundle,
            operation == CAPTURE_REMOTE_HS_CODES,
        ));
    }
    let seed: ApiHsCodeDto =
        serde_json::from_value(body.clone()).map_err(|error| invalid(error.to_string()))?;
    export_doc_hs::trusted_url(&seed.detail_url).map_err(invalid)?;
    let detail = export_doc_hs::detail(&seed, &observed, &check).map_err(unavailable)?;
    operation::check()?;
    if operation == FETCH_REMOTE_HS_CODE_DETAIL {
        return Ok(serde_json::to_value(dto_from_detail(&detail))?);
    }
    resolve(service, actor, &seed, &detail, &observed)
}

fn search_bundle(keyword: &str, observed: &str) -> Result<SearchBundle> {
    let initial = export_doc_hs::search(keyword, observed, &check).map_err(unavailable)?;
    enrich(keyword, initial, observed)
}

fn enrich(keyword: &str, initial: SearchBundle, observed: &str) -> Result<SearchBundle> {
    let mut records = initial.records;
    let mut replacements = initial.replacements;
    let mut visited_queries = BTreeSet::from([normalize_identity(keyword)]);
    let mut visited_details = BTreeSet::new();
    let mut detail_lookups = 0usize;

    for depth in 0..=MAX_RECOMMENDATION_DEPTH {
        operation::check()?;
        let recommended = recommended_from_records(&records, &replacements);
        let obsolete_details = records
            .iter()
            .filter(|record| {
                record.kind == RemoteRecordKind::DeclarationExample
                    && !record.item.detail_url.is_empty()
            })
            .map(|record| record.item.normalized_code.clone())
            .filter(|code| visited_details.insert(code.clone()))
            .take(MAX_DETAIL_LOOKUPS.saturating_sub(detail_lookups))
            .collect::<Vec<_>>();
        let mut additions = Vec::new();
        for code in obsolete_details {
            if detail_lookups >= MAX_DETAIL_LOOKUPS {
                break;
            }
            let Some(record) = records
                .iter()
                .find(|record| record.item.normalized_code == code)
                .cloned()
            else {
                continue;
            };
            detail_lookups += 1;
            if let Ok(detail) = export_doc_hs::detail(&record.item, observed, &check) {
                if !detail.expired {
                    additions.push(RemoteRecord {
                        item: detail.item.clone(),
                        kind: RemoteRecordKind::StandardCode,
                        expired: false,
                        instance_count: detail.instance_count,
                        summary_url: record.summary_url,
                        evidence_url: detail.evidence_url.clone(),
                    });
                }
                if !detail.recommended_keywords.is_empty() {
                    replacements.push(export_doc_hs::parser::ReplacementEvidence {
                        old_code: record.item.normalized_code,
                        recommended_keywords: detail.recommended_keywords,
                        evidence_url: detail.evidence_url,
                    });
                }
            }
        }
        records.extend(additions);
        if depth == MAX_RECOMMENDATION_DEPTH || has_current_standard(&records) {
            break;
        }
        for recommendation in recommended {
            let identity = normalize_identity(&recommendation);
            if !visited_queries.insert(identity) {
                continue;
            }
            operation::check()?;
            if let Ok(nested) = export_doc_hs::search(&recommendation, observed, &check) {
                records.extend(nested.records);
                replacements.extend(nested.replacements);
            }
            if has_current_standard(&records) {
                break;
            }
        }
    }
    Ok(SearchBundle {
        records: deduplicate(records),
        replacements,
    })
}

fn normalize_identity(value: &str) -> String {
    rules::code(value)
        .map(|code| code.to_ascii_lowercase())
        .unwrap_or_else(|_| rules::text(value).to_ascii_lowercase())
}

fn has_current_standard(records: &[RemoteRecord]) -> bool {
    records
        .iter()
        .any(|record| record.kind == RemoteRecordKind::StandardCode && !record.expired)
}

fn recommended_from_records(
    records: &[RemoteRecord],
    replacements: &[export_doc_hs::parser::ReplacementEvidence],
) -> Vec<String> {
    let mut values = BTreeSet::new();
    for item in replacements {
        values.extend(item.recommended_keywords.iter().cloned());
    }
    for record in records {
        if let Some(items) = record
            .item
            .recommended_keywords
            .as_ref()
            .and_then(Value::as_array)
        {
            values.extend(items.iter().filter_map(Value::as_str).map(str::to_string));
        }
    }
    values
        .into_iter()
        .filter(|value| !value.trim().is_empty())
        .collect()
}

fn deduplicate(records: Vec<RemoteRecord>) -> Vec<RemoteRecord> {
    let mut seen = BTreeSet::new();
    records
        .into_iter()
        .filter(|record| {
            seen.insert(
                format!(
                    "{}|{}|{}|{}",
                    record.kind.as_str(),
                    record.item.normalized_code,
                    record.item.name.trim(),
                    record.item.description.trim()
                )
                .to_ascii_lowercase(),
            )
        })
        .collect()
}

fn search_response(bundle: &SearchBundle, captured: bool) -> Value {
    let standard = standard_items(bundle);
    let examples = bundle
        .records
        .iter()
        .filter(|record| record.kind == RemoteRecordKind::DeclarationExample)
        .count();
    json!({
        "items": standard,
        "count": standard.len(),
        "source": export_doc_hs::SOURCE,
        "storagePolicy": if captured {
            "联网查询结果已返回,申报实例已进入待审核候选池;确认后才会进入正式共享实例库。"
        } else {
            "远程HS编码查询只读取在线来源;不会写入候选池。需要进入待审核候选池时,请使用“查询并加入候选”。"
        },
        "standardCodeCount": standard.len(),
        "declarationExampleCount": examples
    })
}

fn standard_items(bundle: &SearchBundle) -> Vec<ApiHsCodeDto> {
    let mut grouped = BTreeMap::<String, RemoteRecord>::new();
    for record in bundle
        .records
        .iter()
        .filter(|record| record.kind == RemoteRecordKind::StandardCode && !record.expired)
    {
        let code = record.item.normalized_code.clone();
        let replace = grouped.get(&code).is_none_or(|previous| {
            record.instance_count.is_some() && previous.instance_count.is_none()
                || !record.item.description.trim().is_empty()
                    && previous.item.description.trim().is_empty()
        });
        if replace {
            grouped.insert(code, record.clone());
        }
    }
    grouped.into_values().map(dto_from_record).collect()
}

fn dto_from_record(record: RemoteRecord) -> ApiHsCodeDto {
    let mut item = record.item;
    item.remote_record_kind = record.kind.as_str().into();
    item.instance_count = record.instance_count;
    item.summary_url = record.summary_url;
    item.evidence_url = record.evidence_url;
    item
}

fn dto_from_detail(detail: &export_doc_hs::parser::DetailBundle) -> ApiHsCodeDto {
    let mut item = detail.item.clone();
    item.remote_record_kind = RemoteRecordKind::StandardCode.as_str().into();
    item.instance_count = detail.instance_count;
    item.summary_url = if detail.evidence_url.is_empty() {
        item.detail_url.clone()
    } else {
        detail.evidence_url.clone()
    };
    item.evidence_url = detail.evidence_url.clone();
    item.recommended_keywords = Some(
        detail
            .recommended_keywords
            .iter()
            .map(|value| json!(value))
            .collect(),
    );
    item.personal_postal_tax_code = detail.personal_postal_tax_code.clone();
    item.ciq_entries = Some(
        detail
            .ciq_entries
            .iter()
            .map(|entry| json!({"code": entry.code, "name": entry.name}))
            .collect(),
    );
    item.classification_entries = Some(
        detail
            .classification_entries
            .iter()
            .map(|entry| json!({"code": entry.code, "name": entry.name}))
            .collect(),
    );
    item.declaration_example_count = detail.declaration_examples.len() as i64;
    item
}

fn resolve(
    service: &NativeService,
    actor: &Actor,
    seed: &ApiHsCodeDto,
    detail: &export_doc_hs::parser::DetailBundle,
    observed: &str,
) -> Result<Value> {
    if !detail.expired {
        capture_detail(service, actor, seed, detail)?;
        return Ok(json!({
            "items": [dto_from_detail(detail)],
            "removedItems": [],
            "updatedCount": 1,
            "removedCount": 0,
            "message": if detail.declaration_examples.is_empty() {
                "已补全HS编码详情。"
            } else {
                "已补全HS详情,并提取申报实例。"
            },
            "storagePolicy": "HS编码联网详情补全只访问在线来源并沉淀待审核申报实例;第三方标准编码不会自动写成当前年度有效税则,过期编码只从本次结果中清理,不新增默认目录或系统 C 盘落点。"
        }));
    }
    let mut replacement_items = Vec::<ApiHsCodeDto>::new();
    for keyword in detail
        .recommended_keywords
        .iter()
        .take(MAX_RECOMMENDATION_DEPTH)
    {
        operation::check()?;
        let bundle = search_bundle(keyword, observed)?;
        let items = standard_items(&bundle);
        if items.is_empty() {
            continue;
        }
        replacement_items.extend(items);
        break;
    }
    let removed = dto_from_detail(detail);
    Ok(json!({
        "items": replacement_items,
        "removedItems": [removed],
        "updatedCount": replacement_items.len(),
        "removedCount": 1,
        "message": if replacement_items.is_empty() {
            "原编码已作废,暂未在当前来源找到可验证的替代编码。"
        } else {
            "原编码已作废,已按网页推荐链补入当前编码候选。"
        },
        "storagePolicy": "HS编码联网详情补全只访问在线来源并沉淀待审核申报实例;第三方标准编码不会自动写成当前年度有效税则,过期编码只从本次结果中清理,不新增默认目录或系统 C 盘落点。"
    }))
}

fn capture(
    service: &NativeService,
    actor: &Actor,
    query: &str,
    bundle: &SearchBundle,
) -> Result<()> {
    service.store.transaction(|tx| {
        let codes = hs::codes(tx)?;
        let relations = store::all(tx, hs::REPLACEMENTS)?;
        for record in bundle
            .records
            .iter()
            .filter(|record| record.kind == RemoteRecordKind::DeclarationExample)
        {
            operation::check()?;
            let raw = rules::code(&record.item.code).map_err(invalid)?;
            let name = export_doc_domain::crm::text(&record.item.name, "参考商品名称", 300, true)
                .map_err(invalid)?;
            let spec =
                export_doc_domain::crm::text(&record.item.description, "参考规格", 1500, false)
                    .map_err(invalid)?;
            let fingerprint = rules::fingerprint(&[&raw, &name, &spec]);
            let previous = tx.find_identity(hs::CANDIDATES, &fingerprint.to_ascii_lowercase())?;
            let id = previous
                .as_ref()
                .and_then(|row| row["id"].as_i64())
                .unwrap_or(0);
            let resolved = hs_search::resolve(&raw, "", false, &codes, &relations);
            let now = store::timestamp();
            let mut row = previous.unwrap_or_else(|| {
                json!({
                    "fingerprint": fingerprint,
                    "rawReportedHsCode": raw,
                    "productName": name,
                    "specification": spec,
                    "source": export_doc_hs::SOURCE,
                    "sourceUrl": record.evidence_url,
                    "reviewStatus": "Pending",
                    "firstSeenAt": now,
                    "reviewedAt": null,
                    "seenCount": 0
                })
            });
            row["queryText"] = json!(query);
            row["lastSeenAt"] = json!(now);
            row["seenCount"] = json!(row["seenCount"].as_i64().unwrap_or(0).saturating_add(1));
            if row["reviewStatus"] == "Pending" {
                row["suggestedCurrentHsCode"] = json!(resolved.code);
                row["resolutionStatus"] = json!(resolved.status);
            }
            store::save(
                tx,
                hs::CANDIDATES,
                id,
                row,
                Some(fingerprint),
                actor,
                if id == 0 { "create" } else { "edit" },
            )?;
        }
        Ok(())
    })
}

fn capture_detail(
    service: &NativeService,
    actor: &Actor,
    seed: &ApiHsCodeDto,
    detail: &export_doc_hs::parser::DetailBundle,
) -> Result<()> {
    if detail.declaration_examples.is_empty() {
        return Ok(());
    }
    let bundle = SearchBundle {
        records: detail.declaration_examples.clone(),
        replacements: if detail.recommended_keywords.is_empty() {
            Vec::new()
        } else {
            vec![export_doc_hs::parser::ReplacementEvidence {
                old_code: seed.normalized_code.clone(),
                recommended_keywords: detail.recommended_keywords.clone(),
                evidence_url: detail.evidence_url.clone(),
            }]
        },
    };
    capture(service, actor, &seed.name, &bundle)
}
