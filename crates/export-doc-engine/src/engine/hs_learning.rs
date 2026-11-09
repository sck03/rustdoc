use super::{
    error::{Result, conflict, invalid},
    hs::{self, read},
    records::text,
    store::{self, Actor},
};
use crate::engine::store::Connection;
use crate::{generated_api::*, operation};
use export_doc_domain::hs as rules;
use serde_json::{Value, json};
use std::collections::BTreeSet;

fn field(body: &Value, key: &str, label: &str, max: usize, required: bool) -> Result<String> {
    export_doc_domain::crm::text(&text(body, key), label, max, required).map_err(invalid)
}
pub fn save_example(tx: &Connection, actor: &Actor, body: &Value) -> Result<Value> {
    let raw = rules::code(&text(body, "rawReportedHsCode")).map_err(invalid)?;
    let current = if text(body, "resolvedCurrentHsCode").is_empty() {
        String::new()
    } else {
        rules::code(&text(body, "resolvedCurrentHsCode")).map_err(invalid)?
    };
    if !current.is_empty() {
        let standard = tx
            .find_identity(hs::CODES, &current)?
            .map(|row| serde_json::from_value::<rules::Standard>(row))
            .transpose()?;
        if !standard.as_ref().is_some_and(rules::Standard::trusted) {
            return Err(invalid("当前有效编码必须来自已验证的本地年度税则。"));
        }
    }
    let name = field(body, "productName", "商品名称", 300, true)?;
    let spec = field(body, "specification", "规格与申报要素", 1500, false)?;
    let source = field(body, "source", "实例来源", 100, false)?;
    let year = body["sourceYear"].as_i64();
    if year.is_some_and(|y| !(2000..=2100).contains(&y)) {
        return Err(invalid("实例年度须为 2000 至 2100。"));
    }
    let fingerprint = rules::fingerprint(&[&raw, &name, &spec]);
    let requested_id = body["id"].as_i64().unwrap_or(0);
    if requested_id < 0 {
        return Err(invalid("实例编号无效。"));
    }
    let previous = if requested_id > 0 {
        Some(store::get(tx, hs::EXAMPLES, requested_id)?)
    } else {
        if tx
            .find_identity(hs::EXAMPLES, &fingerprint.to_lowercase())?
            .is_some()
        {
            return Err(conflict("该商品实例已存在，请刷新后编辑原记录。"));
        }
        None
    };
    let id = previous
        .as_ref()
        .and_then(|r| r["id"].as_i64())
        .unwrap_or(0);
    let status = if text(body, "resolutionStatus").eq_ignore_ascii_case("ManuallyVerified") {
        "ManuallyVerified"
    } else if current.is_empty() {
        "Unresolved"
    } else if current == raw {
        "Active"
    } else {
        "ObsoleteMapped"
    };
    let mut value = json!({"id":id,"fingerprint":fingerprint,"rawReportedHsCode":raw,"resolvedCurrentHsCode":if current.is_empty(){Value::Null}else{json!(current)},"productName":name,"specification":spec,
        "searchText":rules::text(&format!("{name} {spec}")),"source":if source.is_empty(){"Manual"}else{&source},"sourceYear":year,"resolutionStatus":status,"isManuallyVerified":body["isManuallyVerified"]==true,
        "useCount":0,"rejectedCount":0,"lastUsedAt":null});
    if let Some(previous) = previous {
        for key in ["useCount", "rejectedCount", "lastUsedAt"] {
            value[key] = previous[key].clone();
        }
    }
    value["expectedVersion"] = json!(store::expected(body));
    store::save(
        tx,
        hs::EXAMPLES,
        id,
        value,
        Some(fingerprint),
        actor,
        if id == 0 { "create" } else { "edit" },
    )
}
pub fn feedback(tx: &Connection, actor: &Actor, body: &Value, year: i32) -> Result<()> {
    let query = field(body, "queryText", "查询条件", 500, false)?;
    let name = field(body, "productName", "商品名称", 300, false)?;
    let spec = field(body, "specification", "规格与申报要素", 1500, false)?;
    let code = rules::code(&text(body, "candidateCode")).map_err(invalid)?;
    let accepted = body["accepted"]
        .as_bool()
        .ok_or_else(|| invalid("缺少反馈结果。"))?;
    if accepted && !hs::is_current(tx, &code, year)? {
        return Err(invalid(
            "确认适用前必须选择已验证年度税则中的当前有效编码。",
        ));
    }
    let fingerprint = rules::fingerprint(&[&rules::text(&query), &code, &name, &spec]);
    let existing = tx.find_identity(hs::FEEDBACK, &fingerprint.to_lowercase())?;
    let mut value=existing.clone().unwrap_or_else(||json!({"fingerprint":fingerprint,"acceptedCount":0,"rejectedCount":0,"lastConfirmedAt":null}));
    value["queryText"] = json!(query);
    value["productName"] = json!(name);
    value["specification"] = json!(spec);
    value["candidateCode"] = json!(code);
    let key = if accepted {
        "acceptedCount"
    } else {
        "rejectedCount"
    };
    let count = value[key]
        .as_i64()
        .unwrap_or(0)
        .checked_add(1)
        .filter(|n| *n <= i32::MAX as i64)
        .ok_or_else(|| invalid("反馈计数超出范围。"))?;
    value[key] = json!(count);
    if accepted {
        value["lastConfirmedAt"] = json!(store::timestamp());
    }
    if let Some(previous) = &existing {
        value["expectedVersion"] = previous["versionNumber"].clone();
    }
    let id = existing.and_then(|r| r["id"].as_i64()).unwrap_or(0);
    let value = store::save(
        tx,
        hs::FEEDBACK,
        id,
        value,
        Some(fingerprint),
        actor,
        if id == 0 { "create" } else { "edit" },
    )?;
    if accepted && value["acceptedCount"].as_i64().unwrap_or(0) >= 3 {
        let input = json!({"id":0,"rawReportedHsCode":code,"resolvedCurrentHsCode":code,"productName":if name.is_empty(){&query}else{&name},"specification":spec,"source":"ConsensusConfirmed","sourceYear":year,"resolutionStatus":"ManuallyVerified","isManuallyVerified":true});
        // Existing reviewed knowledge owns its provenance; feedback must not
        // overwrite a manual example or redirect it to another tariff code.
        let example_fingerprint =
            rules::fingerprint(&[&code, if name.is_empty() { &query } else { &name }, &spec]);
        if tx
            .find_identity(hs::EXAMPLES, &example_fingerprint.to_lowercase())?
            .is_none()
        {
            save_example(tx, actor, &input)?;
        }
    }
    Ok(())
}
pub fn remote_review(
    tx: &Connection,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
    body: &Value,
    year: i32,
) -> Result<Value> {
    if operation == LIST_HS_CODE_REMOTE_CANDIDATES {
        let status = if read(query, "status").is_empty() {
            "Pending"
        } else {
            read(query, "status")
        };
        if !["Pending", "Confirmed", "Ignored"].contains(&status) {
            return Err(invalid("联网候选状态无效。"));
        }
        let filter = rules::text(read(query, "keyword"));
        let (number, size, offset) = store::page_parameters(query);
        let mut count = 0_i64;
        let mut rows = Vec::with_capacity(size);
        super::hs_queries::visit(
            tx,
            &mut super::hs_queries::query(hs::CANDIDATES, &[]),
            |row| {
                if row["reviewStatus"] == status
                    && (filter.is_empty()
                        || [
                            "productName",
                            "rawReportedHsCode",
                            "specification",
                            "queryText",
                        ]
                        .iter()
                        .any(|key| rules::text(&text(&row, key)).contains(&filter)))
                {
                    if count >= offset && rows.len() < size {
                        rows.push(row);
                    }
                    count += 1;
                }
                Ok(())
            },
        )?;
        let mut value = crate::contracts::page(rows, count as usize, number, size);
        value["reviewStatus"] = json!(status);
        return Ok(value);
    }
    if operation == RESET_HS_CODE_REMOTE_CANDIDATES {
        let ids = hs::ids(body)?;
        if ids.len() > 500 {
            return Err(invalid("一次最多重置 500 条候选。"));
        }
        let mut count = 0;
        for id in ids {
            operation::check()?;
            let Some(mut row) = tx.get(hs::CANDIDATES, id)? else {
                continue;
            };
            if row["reviewStatus"] == "Pending" {
                continue;
            }
            let source = format!("RemoteConfirmed:{id}:{}", text(&row, "source"));
            let fingerprint = rules::fingerprint(&[
                &text(&row, "rawReportedHsCode"),
                &text(&row, "productName"),
                &text(&row, "specification"),
            ]);
            if row["reviewStatus"] == "Confirmed" {
                if let Some(example) = tx
                    .find_identity(hs::EXAMPLES, &fingerprint.to_lowercase())?
                    .filter(|r| r["source"] == source)
                {
                    hs::delete(
                        tx,
                        actor,
                        hs::EXAMPLES,
                        &BTreeSet::from([example["id"].as_i64().unwrap_or(0)]),
                    )?;
                }
            }
            row["reviewStatus"] = json!("Pending");
            row["reviewedAt"] = Value::Null;
            store::save(
                tx,
                hs::CANDIDATES,
                id,
                row,
                Some(fingerprint),
                actor,
                "edit",
            )?;
            count += 1;
        }
        return Ok(
            json!({"success":true,"message":format!("已重置 {count} 条候选。"),"count":count}),
        );
    }
    let inputs = if operation == REVIEW_HS_CODE_REMOTE_CANDIDATE {
        vec![body.clone()]
    } else if operation == REVIEW_HS_CODE_REMOTE_CANDIDATES_BATCH {
        body["items"]
            .as_array()
            .cloned()
            .ok_or_else(|| invalid("缺少候选列表。"))?
    } else {
        return Err(invalid("知识库操作无效。"));
    };
    if inputs.len() > 200 {
        return Err(invalid("一次最多审核 200 条候选。"));
    }
    let codes = hs::codes_for_year(tx, year)?;
    let mut count = 0;
    for input in inputs {
        operation::check()?;
        let id = input["id"].as_i64().unwrap_or(0);
        let mut row = store::get(tx, hs::CANDIDATES, id)?;
        if row["reviewStatus"] != "Pending" {
            continue;
        }
        let accepted = input["confirmed"] == true;
        if accepted {
            let current = rules::code(&text(&input, "currentCode")).map_err(invalid)?;
            if !codes.get(&current).is_some_and(rules::Standard::trusted) {
                return Err(invalid("请选择经过验证的当前有效编码。"));
            }
            let raw = text(&row, "rawReportedHsCode");
            let name = text(&row, "productName");
            let spec = text(&row, "specification");
            let fingerprint = rules::fingerprint(&[&raw, &name, &spec]);
            if let Some(existing) = tx.find_identity(hs::EXAMPLES, &fingerprint.to_lowercase())? {
                if existing["resolvedCurrentHsCode"]
                    .as_str()
                    .is_some_and(|c| !c.is_empty() && c != current)
                {
                    return Err(conflict(
                        "同一商品已有指向其他编码的正式实例，请先处理冲突。",
                    ));
                }
            } else {
                save_example(
                    tx,
                    actor,
                    &json!({"rawReportedHsCode":raw,"resolvedCurrentHsCode":current,"productName":name,"specification":spec,"source":format!("RemoteConfirmed:{id}:{}",text(&row,"source")),"resolutionStatus":"ManuallyVerified","isManuallyVerified":true}),
                )?;
            }
            row["suggestedCurrentHsCode"] = json!(current);
            row["resolutionStatus"] = json!("ManuallyVerified");
        }
        row["reviewStatus"] = json!(if accepted { "Confirmed" } else { "Ignored" });
        row["reviewedAt"] = json!(store::timestamp());
        let fingerprint = text(&row, "fingerprint");
        store::save(
            tx,
            hs::CANDIDATES,
            id,
            row,
            Some(fingerprint),
            actor,
            "edit",
        )?;
        count += 1;
    }
    Ok(json!({"success":true,"message":format!("已审核 {count} 条候选。"),"count":count}))
}
