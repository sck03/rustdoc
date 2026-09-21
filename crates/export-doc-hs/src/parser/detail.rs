use super::*;

fn following_table<'a>(document: &'a Html, labels: &[&str]) -> Option<ElementRef<'a>> {
    let selector = select("div,h1,h2,h3,h4,caption,header,section");
    for node in document.select(&selector) {
        let own = normalize_text(&node.text().collect::<Vec<_>>().join(" "));
        if own.is_empty() || own.chars().count() > 240 {
            continue;
        }
        if labels.iter().any(|label| own.contains(label)) {
            if let Some(table) = node.select(&select("table")).next() {
                return Some(table);
            }
            let mut sibling = node.next_sibling();
            while let Some(value) = sibling {
                if let Some(element) = ElementRef::wrap(value) {
                    if element.value().name() == "table" {
                        return Some(element);
                    }
                    if let Some(table) = element.select(&select("table")).next() {
                        return Some(table);
                    }
                }
                sibling = value.next_sibling();
            }
        }
    }
    None
}
fn heading_value(document: &Html, label: &str) -> String {
    let selector = select("div,h1,h2,h3,h4,header,section");
    document
        .select(&selector)
        .find_map(|node| {
            let value = normalize_text(&node.text().collect::<Vec<_>>().join(" "));
            (value.contains(label) && value.chars().count() <= 160).then(|| {
                value
                    .replace(label, "")
                    .trim()
                    .trim_matches(['「', '」', '[', ']'])
                    .to_string()
            })
        })
        .unwrap_or_default()
}
fn reference_entries(table: Option<ElementRef<'_>>) -> Vec<RemoteReferenceEntry> {
    let Some(table) = table else {
        return Vec::new();
    };
    table
        .select(&select("tr"))
        .filter_map(|row| {
            let cells = row.select(&select("td,th")).map(text).collect::<Vec<_>>();
            if cells.len() < 2 {
                return None;
            }
            let code = cells[0].trim();
            let name = cells[1].trim();
            if code.is_empty()
                || name.is_empty()
                || (code.contains("编码") && (name.contains("名称") || name.contains("信息")))
            {
                return None;
            }
            Some(RemoteReferenceEntry {
                code: code.to_string(),
                name: name.to_string(),
            })
        })
        .collect()
}
pub fn detail(html: &str, seed: &ApiHsCodeDto, observed: &str) -> Result<DetailBundle, String> {
    let document = Html::parse_document(html);
    let root = document
        .select(&select("#hscode-detail"))
        .next()
        .or_else(|| {
            best_table(
                &document,
                |table| {
                    let content = text(table);
                    ["商品编码", "商品名称", "申报要素", "法定第一单位"]
                        .iter()
                        .filter(|label| content.contains(**label))
                        .count() as i64
                },
                3,
            )
        })
        .unwrap_or_else(|| document.root_element());
    let mut fields = std::collections::BTreeMap::new();
    for row in root.select(&select("tr")) {
        let cells = row.select(&select("th,td")).map(text).collect::<Vec<_>>();
        for pair in cells.chunks_exact(2) {
            fields
                .entry(pair[0].trim_end_matches([':', '：']).trim().to_string())
                .or_insert_with(|| pair[1].clone());
        }
    }
    let read = |labels: &[&str]| {
        labels
            .iter()
            .find_map(|label| fields.get(*label))
            .cloned()
            .unwrap_or_default()
    };
    let code = read(&["商品编码", "HS编码"]);
    let name = read(&["商品名称", "品名"]);
    if code.is_empty() && name.is_empty() {
        return Err("详情页面格式变化或需要交互验证,未读取到编码资料。".into());
    }
    let mut item = seed.clone();
    if let Some(code) = leading(&code) {
        item.code = code.clone();
        item.normalized_code = code;
    }
    if !name.is_empty() {
        item.name = name;
    }
    for (target, labels) in [
        (&mut item.elements, vec!["申报要素", "规范申报要素"]),
        (&mut item.unit, vec!["法定第一单位", "第一法定单位"]),
        (&mut item.rebate_rate, vec!["出口退税率", "退税率"]),
        (
            &mut item.supervision_conditions,
            vec!["海关监管条件", "监管条件"],
        ),
        (
            &mut item.inspection_category,
            vec!["检验检疫类别", "检验检疫"],
        ),
        (
            &mut item.normal_tariff_rate,
            vec!["普通进口税率", "普通税率"],
        ),
        (
            &mut item.preferential_tariff_rate,
            vec!["最惠国进口税率", "优惠税率", "最惠国税率"],
        ),
        (&mut item.consumption_tax_rate, vec!["消费税率"]),
        (
            &mut item.value_added_tax_rate,
            vec!["增值税率", "进口增值税率"],
        ),
        (&mut item.export_tariff_rate, vec!["出口关税率", "出口税率"]),
        (&mut item.description, vec!["英文名称", "英文品名"]),
    ] {
        let value = read(&labels);
        if !value.is_empty() {
            *target = value;
        }
    }
    let expired = text(root).contains("已作废");
    item.status = if expired { "Obsolete" } else { "ReferenceOnly" }.into();
    item.source_name = source_name(RemoteRecordKind::StandardCode).into();
    item.last_verified_at = Some(observed.into());
    item.observed_at = Some(observed.into());
    item.update_time = Some(observed.into());
    let recommendations = if item.code.is_empty() {
        Vec::new()
    } else {
        recommended_keywords(&document.root_element().html())
    };
    let example_url = if seed.detail_url.is_empty() {
        String::new()
    } else {
        format!("{}#sbsl", seed.detail_url.split('#').next().unwrap_or(""))
    };
    let examples = parse_declaration_table(
        following_table(&document, &["申报实例汇总", "申报实例", "申报案例"]),
        observed,
        &example_url,
        &item.code,
    );
    Ok(DetailBundle {
        item,
        expired,
        instance_count: seed.instance_count,
        recommended_keywords: recommendations,
        declaration_examples: examples,
        personal_postal_tax_code: heading_value(&document, "个人行邮税号"),
        ciq_entries: reference_entries(following_table(
            &document,
            &["10位HS编码+3位CIQ", "CIQ代码"],
        )),
        classification_entries: reference_entries(following_table(
            &document,
            &["所属分类及章节", "所属分类", "章节、品目"],
        )),
        evidence_url: seed.detail_url.clone(),
    })
}
