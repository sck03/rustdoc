//! Tariff reconciliation has no database writes; replacement names are indexed once.
use super::*;

pub(super) struct Input<'a> {
    pub table: &'a export_doc_excel::TableData,
    pub header: usize,
    pub mapping: &'a [(&'static str, usize)],
    pub source: &'a str,
    pub year: i64,
    pub verified_at: &'a str,
    pub complete: bool,
}

pub(super) fn items(
    input: &Input<'_>,
    existing: &BTreeMap<String, ApiHsCodeDto>,
) -> Result<Vec<Value>> {
    let mut items = vec![];
    let mut seen = BTreeMap::<String, (Value, String)>::new();
    let mut conflicts = BTreeSet::new();
    for (index, row) in input.table.rows.iter().enumerate().skip(input.header + 1) {
        operation::check()?;
        if row.iter().all(|value| value.trim().is_empty()) {
            continue;
        }
        let mut item = imported_item(input, row, existing)?;
        item["status"] = json!("Active");
        item["sourceName"] = json!(input.source);
        item["effectiveYear"] = json!(input.year);
        item["lastVerifiedAt"] = json!(input.verified_at);
        let parsed: Result<ApiHsCodeDto> = serde_json::from_value(item.clone())
            .map_err(|cause| invalid(cause.to_string()))
            .and_then(|mut dto| {
                rules::normalize(&mut dto).map_err(invalid)?;
                Ok(dto)
            });
        let (mut change, mut message, mut changed) = ("Add", String::new(), vec![]);
        match parsed {
            Err(cause) => {
                change = "Invalid";
                message = cause.message;
            }
            Ok(dto) => {
                item = serde_json::to_value(&dto)?;
                let facts = comparable(&item);
                if let Some((prior, _)) = seen.get(&dto.code) {
                    if *prior != facts {
                        conflicts.insert(dto.code.clone());
                    } else {
                        change = "Unchanged";
                    }
                }
                seen.insert(dto.code.clone(), (facts.clone(), rules::text(&dto.name)));
                if let Some(old) = existing.get(&dto.code) {
                    let old = serde_json::to_value(old)?;
                    item["id"] = old["id"].clone();
                    item["rowVersion"] = old["rowVersion"].clone();
                    changed = facts
                        .as_object()
                        .expect("comparable object")
                        .iter()
                        .filter(|(key, value)| old[*key] != **value)
                        .map(|(key, value)| field_change(key, &old[key], value))
                        .collect();
                    if old["effectiveYear"]
                        .as_i64()
                        .is_some_and(|year| year > input.year)
                    {
                        change = "Conflict";
                        message = "已有较新年度资料，不能用旧年度覆盖。".into();
                    } else if change != "Unchanged" {
                        change = if changed.is_empty() {
                            "Unchanged"
                        } else {
                            "Update"
                        };
                    }
                }
            }
        }
        items.push(json!({"changeType":change,"rowNumber":index+input.table.first_row,"item":item,"changes":changed,"replacementCandidates":[],"message":message}));
    }
    if items.is_empty() {
        return Err(invalid("税则文件没有可读取的明细。"));
    }
    for item in &mut items {
        if conflicts.contains(&text(&item["item"], "code")) {
            item["changeType"] = json!("Conflict");
            item["message"] = json!("同一编码在文件中存在不同内容，不会写入。");
        }
    }
    if input.complete {
        if items
            .iter()
            .any(|row| matches!(row["changeType"].as_str(), Some("Invalid" | "Conflict")))
        {
            return Err(invalid(
                "完整年度库包含无效或冲突行，请修正后重新预检，避免误判缺失编码。",
            ));
        }
        let mut replacements = BTreeMap::<&str, Vec<&str>>::new();
        for (code, (_, name)) in &seen {
            operation::check()?;
            let codes = replacements.entry(name).or_default();
            if codes.len() < 20 {
                codes.push(code);
            }
        }
        for (code, old) in existing {
            operation::check()?;
            if seen.contains_key(code)
                || old.status != "Active"
                || old.effective_year.is_none_or(|year| year > input.year)
            {
                continue;
            }
            let candidates = replacements
                .get(rules::text(&old.name).as_str())
                .cloned()
                .unwrap_or_default();
            items.push(json!({"changeType":"SuspectedObsolete","rowNumber":0,"item":old,"changes":[field_change("status", &json!(old.status), &json!("SuspectedObsolete"))],"replacementCandidates":candidates,"message":"完整年度文件未出现，确认后标记为疑似作废，保留原编码。"}));
        }
    }
    Ok(items)
}

fn imported_item(
    input: &Input<'_>,
    row: &[String],
    existing: &BTreeMap<String, ApiHsCodeDto>,
) -> Result<Value> {
    let provided: BTreeMap<_, _> = input
        .mapping
        .iter()
        .map(|(field, column)| (*field, row.get(*column).map(String::as_str).unwrap_or("")))
        .collect();
    let previous = provided
        .get("code")
        .and_then(|code| rules::code(code).ok())
        .and_then(|code| existing.get(&code));
    let mut item = if !input.complete
        && let Some(previous) = previous
    {
        serde_json::to_value(previous)?
    } else {
        contracts::initial(contracts::schema("ApiHsCodeDto"))
    };
    // Missing columns preserve incremental data; an explicitly empty cell clears it.
    let unit = text(&item, "unit");
    let (first, second) = unit.split_once('/').unwrap_or((&unit, ""));
    for (field, value) in &provided {
        item[*field] = json!(value);
    }
    if provided.contains_key("unit") || provided.contains_key("unit2") {
        let first = hs_import::unit(provided.get("unit").copied().unwrap_or(first));
        let second = hs_import::unit(provided.get("unit2").copied().unwrap_or(second));
        item["unit"] = json!(if second.is_empty() || first == second {
            first
        } else {
            format!("{first}/{second}")
        });
    }
    Ok(item)
}

fn field_change(field: &str, before: &Value, after: &Value) -> Value {
    let display = |value: &Value| {
        if value.is_null() {
            String::new()
        } else {
            value
                .as_str()
                .map(str::to_owned)
                .unwrap_or_else(|| value.to_string())
        }
    };
    json!({"field":field,"before":display(before),"after":display(after)})
}

fn comparable(item: &Value) -> Value {
    let keys = hs_import::COLUMNS
        .iter()
        .map(|(key, _)| *key)
        .filter(|key| *key != "unit2")
        .chain(["status", "sourceName", "effectiveYear"]);
    Value::Object(
        keys.map(|key| (key.to_string(), item[key].clone()))
            .collect(),
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn incremental_columns_preserve_missing_values_and_show_explicit_clears() {
        let old = ApiHsCodeDto {
            id: 1,
            code: "6109100000".into(),
            name: "旧名称".into(),
            unit: "本/千克".into(),
            rebate_rate: "13%".into(),
            notes: "人工备注".into(),
            elements: "已核实要素".into(),
            status: "Active".into(),
            effective_year: Some(2026),
            source_name: "年度税则".into(),
            row_version: "native:7".into(),
            ..Default::default()
        };
        let existing = BTreeMap::from([(old.code.clone(), old)]);
        let make = |header: Vec<&str>, row: Vec<&str>, complete| {
            let table = export_doc_excel::TableData {
                rows: vec![
                    header.into_iter().map(str::to_owned).collect(),
                    row.into_iter().map(str::to_owned).collect(),
                ],
                first_row: 1,
                sheet_name: "年度".into(),
            };
            let (header, mapping) = hs_import::headers(&table.rows).unwrap();
            items(
                &Input {
                    table: &table,
                    header,
                    mapping: &mapping,
                    source: "年度税则",
                    year: 2026,
                    verified_at: "2026-01-01T00:00:00Z",
                    complete,
                },
                &existing,
            )
            .unwrap()
            .remove(0)
        };
        let preserved = make(
            vec!["HS编码", "商品名称"],
            vec!["6109100000", "新名称"],
            false,
        );
        assert_eq!(preserved["item"]["notes"], "人工备注");
        assert_eq!(preserved["item"]["rebateRate"], "13%");
        assert_eq!(preserved["item"]["elements"], "已核实要素");
        assert_eq!(preserved["item"]["unit"], "本/千克");
        assert_eq!(preserved["item"]["rowVersion"], "native:7");
        assert_eq!(
            preserved["changes"],
            json!([{"field":"name","before":"旧名称","after":"新名称"}])
        );
        let cleared = make(
            vec!["HS编码", "商品名称", "备注"],
            vec!["6109100000", "旧名称", ""],
            false,
        );
        assert_eq!(cleared["item"]["notes"], "");
        assert_eq!(cleared["item"]["rebateRate"], "13%");
        assert_eq!(
            cleared["changes"],
            json!([{"field":"notes","before":"人工备注","after":""}])
        );
        let unit = make(
            vec!["HS编码", "商品名称", "法定第二单位"],
            vec!["6109100000", "旧名称", "35"],
            false,
        );
        assert_eq!(unit["item"]["unit"], "本/千克");
        let replaced = make(
            vec!["HS编码", "商品名称"],
            vec!["6109100000", "新名称"],
            true,
        );
        assert_eq!(replaced["item"]["notes"], "");
        assert!(
            replaced["changes"]
                .as_array()
                .unwrap()
                .iter()
                .any(|change| change["field"] == "rebateRate")
        );
    }
    #[test]
    fn annual_reconciliation_normalizes_units_and_names_and_bounds_replacements() {
        let mut rows = vec![vec![
            "HS编码".into(),
            "商品名称".into(),
            "法定第一单位".into(),
            "法定第二单位".into(),
        ]];
        for number in 0..1000 {
            rows.push(vec![format!("6109{number:06}"), "E\u{301}cole".into()]);
        }
        rows[1].extend(["132".into(), "35".into()]);
        rows[2].push("137".into());
        let table = export_doc_excel::TableData {
            rows,
            first_row: 1,
            sheet_name: "年度".into(),
        };
        let (header, mapping) = hs_import::headers(&table.rows).unwrap();
        let old = ApiHsCodeDto {
            id: 1,
            code: "9990000000".into(),
            name: "école".into(),
            status: "Active".into(),
            effective_year: Some(2025),
            row_version: "native:1".into(),
            ..Default::default()
        };
        let existing = BTreeMap::from([(old.code.clone(), old)]);
        let input = Input {
            table: &table,
            header,
            mapping: &mapping,
            source: "年度税则",
            year: 2026,
            verified_at: "2026-01-01T00:00:00Z",
            complete: true,
        };
        let items = items(&input, &existing).unwrap();
        assert_eq!(items[0]["item"]["unit"], "本/千克");
        assert_eq!(items[1]["item"]["unit"], "137");
        assert_eq!(items[0]["changeType"], "Add");
        let missing = items.last().unwrap();
        assert_eq!(missing["changeType"], "SuspectedObsolete");
        assert_eq!(
            missing["replacementCandidates"].as_array().unwrap().len(),
            20
        );
        assert_eq!(missing["replacementCandidates"][0], "6109000000");
        assert_eq!(missing["replacementCandidates"][19], "6109000019");
    }
}
