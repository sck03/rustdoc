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
use std::collections::BTreeMap;

pub const OPERATIONS: &[Operation] = &[
    SEARCH_REMOTE_HS_CODES,
    CAPTURE_REMOTE_HS_CODES,
    FETCH_REMOTE_HS_CODE_DETAIL,
    RESOLVE_REMOTE_HS_CODE_DETAIL,
    GET_HS_CODE_REMOTE_HEALTH,
];

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
    let detail = export_doc_hs::detail(&seed, &observed, &check);
    operation::check()?;
    let detail = detail.map_err(unavailable)?;
    if operation == FETCH_REMOTE_HS_CODE_DETAIL {
        return Ok(serde_json::to_value(dto_from_detail(&detail))?);
    }
    resolve(service, actor, &seed, &detail, &observed)
}

fn search_bundle(keyword: &str, observed: &str) -> Result<SearchBundle> {
    struct Source<'a> {
        observed: &'a str,
    }
    impl export_doc_hs::lookup::Source for Source<'_> {
        fn search(&mut self, keyword: &str) -> std::result::Result<SearchBundle, String> {
            export_doc_hs::search(keyword, self.observed, &check)
        }
        fn detail(
            &mut self,
            seed: &ApiHsCodeDto,
        ) -> std::result::Result<export_doc_hs::parser::DetailBundle, String> {
            export_doc_hs::detail(seed, self.observed, &check)
        }
    }
    let result = export_doc_hs::lookup::search(keyword, &mut Source { observed }, &check);
    operation::check()?;
    result.map_err(unavailable)
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
        "source": "remote",
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
    let mut positions = BTreeMap::<String, usize>::new();
    let mut grouped: Vec<RemoteRecord> = Vec::new();
    for record in bundle
        .records
        .iter()
        .filter(|record| record.kind == RemoteRecordKind::StandardCode && !record.expired)
    {
        let code = record.item.normalized_code.clone();
        if let Some(&index) = positions.get(&code) {
            let rank = |r: &RemoteRecord| {
                (
                    r.instance_count.is_some(),
                    !r.item.description.trim().is_empty(),
                )
            };
            if rank(record) > rank(&grouped[index]) {
                grouped[index] = record.clone();
            }
        } else {
            positions.insert(code, grouped.len());
            grouped.push(record.clone());
        }
    }
    grouped.into_iter().map(dto_from_record).collect()
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
    capture_detail(service, actor, seed, detail)?;
    if !detail.expired {
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
    for keyword in &detail.recommended_keywords {
        operation::check()?;
        let bundle = search_bundle(keyword, observed)?;
        capture(service, actor, &seed.name, &bundle)?;
        let items = standard_items(&bundle);
        for item in items {
            let replacement = export_doc_hs::detail(&item, observed, &check);
            operation::check()?;
            let replacement = replacement.map_err(unavailable)?;
            capture_detail(service, actor, seed, &replacement)?;
            if !replacement.expired {
                replacement_items.push(dto_from_detail(&replacement));
            }
        }
        if !replacement_items.is_empty() {
            break;
        }
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn response_preserves_source_order_and_prefers_instance_evidence_before_description() {
        let record = |code: &str, count: Option<i64>, description: &str| RemoteRecord {
            item: ApiHsCodeDto {
                code: code.into(),
                normalized_code: code.into(),
                description: description.into(),
                ..Default::default()
            },
            kind: RemoteRecordKind::StandardCode,
            expired: false,
            instance_count: count,
            summary_url: String::new(),
            evidence_url: String::new(),
        };
        let bundle = SearchBundle {
            records: vec![
                record("6109909000", Some(7), ""),
                record("6109100000", Some(0), "cotton"),
                record("6109909000", None, "inferior duplicate"),
            ],
            replacements: vec![],
        };
        let response = search_response(&bundle, false);
        assert_eq!(response["source"], "remote");
        assert_eq!(response["items"][0]["code"], "6109909000");
        assert_eq!(response["items"][0]["instanceCount"], 7);
        assert_eq!(response["items"][1]["code"], "6109100000");
    }
}
