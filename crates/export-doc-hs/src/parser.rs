use export_doc_contracts::generated_api::ApiHsCodeDto;
use export_doc_domain::hs;
use scraper::{ElementRef, Html, Selector};
use std::collections::BTreeSet;

#[derive(Clone, Debug, PartialEq)]
pub struct RemoteReferenceEntry {
    pub code: String,
    pub name: String,
}

#[derive(Clone, Debug, PartialEq)]
pub struct RemoteRecord {
    pub item: ApiHsCodeDto,
    pub kind: RemoteRecordKind,
    pub expired: bool,
    pub instance_count: Option<i64>,
    pub summary_url: String,
    pub evidence_url: String,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum RemoteRecordKind {
    StandardCode,
    DeclarationExample,
}

impl RemoteRecordKind {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::StandardCode => "StandardCode",
            Self::DeclarationExample => "DeclarationExample",
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct ReplacementEvidence {
    pub old_code: String,
    pub recommended_keywords: Vec<String>,
    pub evidence_url: String,
}

#[derive(Clone, Debug, PartialEq)]
pub struct SearchBundle {
    pub records: Vec<RemoteRecord>,
    pub replacements: Vec<ReplacementEvidence>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct DetailBundle {
    pub item: ApiHsCodeDto,
    pub expired: bool,
    pub instance_count: Option<i64>,
    pub recommended_keywords: Vec<String>,
    pub declaration_examples: Vec<RemoteRecord>,
    pub personal_postal_tax_code: String,
    pub ciq_entries: Vec<RemoteReferenceEntry>,
    pub classification_entries: Vec<RemoteReferenceEntry>,
    pub evidence_url: String,
}

fn select(value: &str) -> Selector {
    Selector::parse(value).expect("static selector")
}
fn text(element: ElementRef<'_>) -> String {
    normalize_text(&element.text().collect::<Vec<_>>().join(" "))
}
fn normalize_text(value: &str) -> String {
    value
        .split_whitespace()
        .collect::<Vec<_>>()
        .join(" ")
        .trim()
        .to_string()
}
fn leading(value: &str) -> Option<String> {
    let raw: String = value
        .trim()
        .chars()
        .take_while(|c| c.is_ascii_digit() || *c == '.')
        .collect();
    hs::code(&raw).ok().filter(|c| c.len() >= 4)
}
fn plausible(code: &str) -> bool {
    (8..=13).contains(&code.len()) && code.chars().all(|c| c.is_ascii_digit())
}
fn header(values: &[String], labels: &[&str]) -> Option<usize> {
    values
        .iter()
        .position(|value| labels.iter().any(|label| value.contains(label)))
}
fn code_header(values: &[String]) -> Option<usize> {
    header(
        values,
        &["HS编码", "海关编码", "商品编码", "税则号列", "税号", "Code"],
    )
}
fn name_header(values: &[String]) -> Option<usize> {
    header(
        values,
        &["商品名称", "货品名称", "货物名称", "品名", "Name"],
    )
}
fn specification_header(values: &[String]) -> Option<usize> {
    header(
        values,
        &[
            "商品规格",
            "规格型号",
            "规格与申报要素",
            "申报规格",
            "规格",
            "Description",
        ],
    )
}
fn instance_header(values: &[String]) -> Option<usize> {
    header(
        values,
        &[
            "实例汇总",
            "实例数量",
            "案例数量",
            "申报实例",
            "实例",
            "案例",
        ],
    )
}
fn header_row(
    rows: &[ElementRef<'_>],
) -> Option<(usize, usize, usize, Option<usize>, Vec<String>)> {
    rows.iter().enumerate().take(8).find_map(|(index, row)| {
        let values = row.select(&select("th,td")).map(text).collect::<Vec<_>>();
        let code = code_header(&values)?;
        let name = name_header(&values)?;
        Some((index, code, name, specification_header(&values), values))
    })
}
fn table_context(table: ElementRef<'_>) -> String {
    let mut parts = vec![
        table.value().id().unwrap_or("").to_string(),
        table.value().attr("class").unwrap_or("").to_string(),
    ];
    if let Some(parent) = table.parent().and_then(ElementRef::wrap) {
        parts.push(parent.value().id().unwrap_or("").to_string());
        parts.push(parent.value().attr("class").unwrap_or("").to_string());
    }
    normalize_text(&parts.join(" "))
}
fn standard_score(table: ElementRef<'_>) -> i64 {
    let rows = table.select(&select("tr")).collect::<Vec<_>>();
    let Some((_, _, _, specification, values)) = header_row(&rows) else {
        return 0;
    };
    let mut score = 8;
    if instance_header(&values).is_some() {
        score += 7;
    }
    if header(&values, &["申报要素", "退税", "编码对比", "税率", "详情"]).is_some() {
        score += 3;
    }
    if table.select(&select("a[href*='#sbsl']")).next().is_some() {
        score += 4;
    }
    let context = table_context(table);
    if context.contains("相关HS编码") || context.contains("税则") {
        score += 3;
    }
    if specification.is_some() && instance_header(&values).is_none() {
        score -= 5;
    }
    score
}
fn declaration_score(table: ElementRef<'_>) -> i64 {
    let rows = table.select(&select("tr")).collect::<Vec<_>>();
    let Some((_, _, _, specification, values)) = header_row(&rows) else {
        return 0;
    };
    let mut score = 8;
    if specification.is_some() {
        score += 7;
    }
    let context = table_context(table);
    if context.contains("申报实例") || context.contains("申报案例") {
        score += 6;
    }
    if header(&values, &["实例汇总", "实例数量", "案例数量", "编码对比"]).is_some()
    {
        score -= 7;
    }
    score
}
fn best_table(
    document: &Html,
    score: impl Fn(ElementRef<'_>) -> i64,
    minimum: i64,
) -> Option<ElementRef<'_>> {
    document
        .select(&select("table"))
        .map(|table| (table, score(table)))
        .filter(|(_, score)| *score >= minimum)
        .max_by_key(|(table, score)| (*score, table.select(&select("tr")).count()))
        .map(|(table, _)| table)
}
fn field_value(cells: &[ElementRef<'_>], index: Option<usize>) -> String {
    index
        .and_then(|index| cells.get(index))
        .copied()
        .map(text)
        .unwrap_or_default()
}
fn detail_url(row: ElementRef<'_>) -> Option<url::Url> {
    row.select(&select("a[href]"))
        .filter_map(|link| link.value().attr("href"))
        .filter_map(|href| crate::trusted_url(href).ok())
        .find(|url| url.path().starts_with("/hscode/detail/"))
}
fn summary_url(row: ElementRef<'_>) -> (String, Option<i64>) {
    for link in row.select(&select("a[href]")) {
        let Some(url) = link
            .value()
            .attr("href")
            .and_then(|href| crate::trusted_url(href).ok())
        else {
            continue;
        };
        if !url.path().starts_with("/hscode/detail/") {
            continue;
        }
        let count = text(link)
            .chars()
            .filter(|c| c.is_ascii_digit() || *c == ',')
            .collect::<String>()
            .replace(',', "")
            .parse::<i64>()
            .ok();
        if count.is_some() || url.fragment() == Some("sbsl") {
            return (url.into(), count);
        }
    }
    (String::new(), None)
}
fn recommended_keywords(value: &str) -> Vec<String> {
    let document = Html::parse_fragment(value);
    let mut result = BTreeSet::new();
    for link in document.select(&select("a[href]")) {
        if let Some(code) = link
            .value()
            .attr("href")
            .and_then(|href| crate::trusted_url(href).ok())
            .and_then(|url| url.path().strip_prefix("/hscode/key/").and_then(leading))
        {
            result.insert(code);
        }
    }
    result.into_iter().collect()
}
fn source_name(kind: RemoteRecordKind) -> &'static str {
    match kind {
        RemoteRecordKind::StandardCode => "i5a6(第三方参考)",
        RemoteRecordKind::DeclarationExample => "i5a6(第三方申报实例)",
    }
}
fn deduplicate(records: Vec<RemoteRecord>) -> Vec<RemoteRecord> {
    let mut seen = BTreeSet::new();
    let mut result = Vec::new();
    for record in records {
        if !plausible(&record.item.normalized_code) || record.item.name.trim().is_empty() {
            continue;
        }
        let key = format!(
            "{}|{}|{}|{}",
            record.kind.as_str(),
            record.item.normalized_code,
            record.item.name.trim(),
            record.item.description.trim()
        )
        .to_lowercase();
        if seen.insert(key) {
            result.push(record);
        }
    }
    result
}
fn parse_standard_table(
    table: ElementRef<'_>,
    observed: &str,
) -> (Vec<RemoteRecord>, Vec<ReplacementEvidence>) {
    let rows = table.select(&select("tr")).collect::<Vec<_>>();
    let Some((header_index, code_index, name_index, specification, headers)) = header_row(&rows)
    else {
        return (Vec::new(), Vec::new());
    };
    let instance = instance_header(&headers);
    let showdesc = select("*[class~='showdesc']");
    let mut records = Vec::new();
    let mut replacements = Vec::new();
    for row in rows.into_iter().skip(header_index + 1) {
        let cells = row.select(&select("td")).collect::<Vec<_>>();
        if cells.len() <= code_index.max(name_index) {
            continue;
        }
        let raw = text(cells[code_index]);
        let Some(code) = leading(&raw) else {
            continue;
        };
        let raw_name = text(cells[name_index]);
        let name = cells[name_index]
            .select(&showdesc)
            .next()
            .map(text)
            .filter(|value| !value.is_empty())
            .unwrap_or_else(|| raw_name.clone());
        let expired = raw.contains("已作废") || name.contains("已作废");
        let description = if let Some(index) = specification {
            field_value(&cells, Some(index))
        } else {
            raw_name
                .split_once('[')
                .and_then(|(_, rest)| rest.split_once(']').map(|(value, _)| value.to_string()))
                .unwrap_or_default()
        };
        let (summary, count) = if let Some(index) = instance {
            cells
                .get(index)
                .copied()
                .map(summary_url)
                .unwrap_or_default()
        } else {
            summary_url(row)
        };
        let item = ApiHsCodeDto {
            code: code.clone(),
            normalized_code: code.clone(),
            name,
            description,
            detail_url: detail_url(row).map(Into::into).unwrap_or_default(),
            status: if expired { "Obsolete" } else { "ReferenceOnly" }.into(),
            source_name: source_name(RemoteRecordKind::StandardCode).into(),
            last_verified_at: Some(observed.into()),
            update_time: Some(observed.into()),
            observed_at: Some(observed.into()),
            ..Default::default()
        };
        let evidence_url = item.detail_url.clone();
        records.push(RemoteRecord {
            item,
            kind: RemoteRecordKind::StandardCode,
            expired,
            instance_count: count,
            summary_url: summary,
            evidence_url,
        });
        if expired {
            let recommendations = recommended_keywords(&cells[code_index].html());
            if !recommendations.is_empty() {
                replacements.push(ReplacementEvidence {
                    old_code: code,
                    recommended_keywords: recommendations,
                    evidence_url: records
                        .last()
                        .map(|record| record.evidence_url.clone())
                        .unwrap_or_default(),
                });
            }
        }
    }
    (records, replacements)
}
fn parse_declaration_table(
    table: Option<ElementRef<'_>>,
    observed: &str,
    evidence_url: &str,
    fallback_code: &str,
) -> Vec<RemoteRecord> {
    let Some(table) = table else {
        return Vec::new();
    };
    let rows = table.select(&select("tr")).collect::<Vec<_>>();
    let Some((header_index, code_index, name_index, specification, _)) = header_row(&rows) else {
        return Vec::new();
    };
    let specification_index = specification.unwrap_or(name_index.max(code_index) + 1);
    let mut records = Vec::new();
    for row in rows.into_iter().skip(header_index + 1) {
        let cells = row.select(&select("td")).collect::<Vec<_>>();
        if cells.len() <= code_index.max(name_index) {
            continue;
        }
        let raw = text(cells[code_index]);
        let code = leading(&raw).or_else(|| leading(fallback_code));
        let Some(code) = code else {
            continue;
        };
        let name = text(cells[name_index]);
        if name.is_empty() {
            continue;
        }
        let description = field_value(&cells, Some(specification_index));
        let url = detail_url(row)
            .map(Into::into)
            .unwrap_or_else(|| evidence_url.to_string());
        records.push(RemoteRecord {
            item: ApiHsCodeDto {
                code: code.clone(),
                normalized_code: code,
                name,
                description,
                detail_url: url.clone(),
                status: "ReferenceOnly".into(),
                source_name: source_name(RemoteRecordKind::DeclarationExample).into(),
                last_verified_at: Some(observed.into()),
                update_time: Some(observed.into()),
                observed_at: Some(observed.into()),
                ..Default::default()
            },
            kind: RemoteRecordKind::DeclarationExample,
            expired: false,
            instance_count: None,
            summary_url: String::new(),
            evidence_url: url,
        });
    }
    records
}
fn mobile_cards(document: &Html, observed: &str) -> Vec<RemoteRecord> {
    let mut records = Vec::new();
    for link in document.select(&select("a[href]")) {
        let Some(url) = link
            .value()
            .attr("href")
            .and_then(|href| crate::trusted_url(href).ok())
            .filter(|url| url.path().starts_with("/hscode/detail/"))
        else {
            continue;
        };
        if link.select(&select("table")).next().is_some() {
            continue;
        }
        let leaves = link
            .select(&select("*"))
            .filter(|node| node.select(&select("*")).next().is_none())
            .map(text)
            .filter(|value| !value.is_empty())
            .collect::<Vec<_>>();
        let code = leaves
            .iter()
            .filter_map(|value| leading(value))
            .find(|code| plausible(code))
            .or_else(|| leading(&text(link)));
        let Some(code) = code else {
            continue;
        };
        let values = leaves
            .into_iter()
            .filter(|value| leading(value).is_none() && !value.contains("查看详情"))
            .collect::<Vec<_>>();
        let Some(name) = values.first().cloned().filter(|value| value.len() <= 300) else {
            continue;
        };
        let description = values.get(1).cloned().unwrap_or_default();
        records.push(RemoteRecord {
            item: ApiHsCodeDto {
                code: code.clone(),
                normalized_code: code,
                name,
                description,
                detail_url: url.to_string(),
                status: "ReferenceOnly".into(),
                source_name: source_name(RemoteRecordKind::DeclarationExample).into(),
                last_verified_at: Some(observed.into()),
                update_time: Some(observed.into()),
                observed_at: Some(observed.into()),
                ..Default::default()
            },
            kind: RemoteRecordKind::DeclarationExample,
            expired: false,
            instance_count: None,
            summary_url: String::new(),
            evidence_url: url.to_string(),
        });
    }
    records
}
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
pub fn empty_result(html: &str) -> bool {
    let document = Html::parse_document(html);
    let value = text(document.root_element());
    ["0 条", "0条", "未找到", "无相关"]
        .iter()
        .any(|needle| value.contains(needle))
}
pub fn search(html: &str, observed: &str) -> Result<SearchBundle, String> {
    let document = Html::parse_document(html);
    let standard = best_table(&document, standard_score, 11);
    let declaration = best_table(&document, declaration_score, 11);
    let mut records = Vec::new();
    let mut replacements = Vec::new();
    if let Some(table) = standard {
        let (parsed, evidence) = parse_standard_table(table, observed);
        records.extend(parsed);
        replacements.extend(evidence);
    }
    records.extend(parse_declaration_table(declaration, observed, "", ""));
    if records.is_empty() {
        records = mobile_cards(&document, observed);
    }
    let records = deduplicate(records);
    replacements.sort_by(|left, right| left.old_code.cmp(&right.old_code));
    replacements.dedup_by(|left, right| left.old_code == right.old_code);
    Ok(SearchBundle {
        records,
        replacements,
    })
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
                9,
            )
        })
        .unwrap_or_else(|| document.root_element());
    let mut fields = std::collections::BTreeMap::new();
    for row in root.select(&select("tr")) {
        let cells = row.select(&select("th,td")).map(text).collect::<Vec<_>>();
        for pair in cells.chunks_exact(2) {
            fields
                .entry(pair[0].trim_matches([':', ':']).to_string())
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
    let evidence_url = if seed.detail_url.is_empty() {
        String::new()
    } else {
        format!("{}#sbsl", seed.detail_url.split('#').next().unwrap_or(""))
    };
    let examples = parse_declaration_table(
        following_table(&document, &["申报实例汇总", "申报实例", "申报案例"]),
        observed,
        &evidence_url,
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
        evidence_url,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    const OBSERVED: &str = "2026-09-20T00:00:00+08:00";

    #[test]
    fn standard_table_keeps_description_separate_from_elements() {
        let html = r#"
        <div id="resultfind">您查询的相关hs编码 15 条</div>
        <table>
          <tr><td>HS编码</td><td>品名</td><td>实例汇总</td><td>申报要素·退税</td><td>编码对比</td></tr>
          <tr>
            <td><b>61083200.00</b></td>
            <td><span class="showdesc">化纤制针织或钩编女睡衣及睡衣裤</span><br/><span>[Knitted women's pyjamas]</span></td>
            <td><a href="//www.i5a6.com/hscode/detail/6108320000#sbsl">534条</a></td>
            <td><a href="//www.i5a6.com/hscode/detail/6108320000">查看详情</a></td>
            <td>--</td>
          </tr>
        </table>
        "#;
        let bundle = search(html, OBSERVED).unwrap();
        let standard = bundle
            .records
            .iter()
            .find(|record| record.kind == RemoteRecordKind::StandardCode)
            .unwrap();
        assert_eq!(standard.item.code, "6108320000");
        assert_eq!(standard.item.name, "化纤制针织或钩编女睡衣及睡衣裤");
        assert_eq!(standard.item.description, "Knitted women's pyjamas");
        assert_eq!(standard.instance_count, Some(534));
        assert_eq!(
            standard.summary_url,
            "https://www.i5a6.com/hscode/detail/6108320000#sbsl"
        );
    }

    #[test]
    fn declaration_table_stays_reference_only_and_uses_description() {
        let html = r#"
        <div id="hssbsl">申报实例查询结果</div>
        <div id="hscasefind"><table>
          <tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>
          <tr><td><a href="//www.i5a6.com/hscode/detail/6109100010">61091000.10</a></td><td>棉制男T恤</td><td>针织|男式|100%棉</td></tr>
        </table></div>
        "#;
        let bundle = search(html, OBSERVED).unwrap();
        let example = bundle
            .records
            .iter()
            .find(|record| record.kind == RemoteRecordKind::DeclarationExample)
            .unwrap();
        assert_eq!(example.item.code, "6109100010");
        assert_eq!(example.item.description, "针织|男式|100%棉");
        assert_eq!(example.item.elements, "");
        assert_eq!(example.item.status, "ReferenceOnly");
    }

    #[test]
    fn detail_reads_reference_entries_and_examples() {
        let examples = (1..=20)
            .map(|index| {
                format!(
                    "<tr><td>61083200.00</td><td>女式睡衣{index}</td><td>针织|女式|100%涤纶</td></tr>"
                )
            })
            .collect::<String>();
        let html = format!(
            r#"
            <div id="hscode-detail"><table>
              <tr><td>商品编码</td><td>61083200.00</td></tr>
              <tr><td>商品名称</td><td>化纤制针织或钩编女睡衣及睡衣裤</td></tr>
              <tr><td>申报要素</td><td>织造方法;类别;成分含量</td></tr>
              <tr><td>法定第一单位</td><td>件</td></tr>
            </table></div>
            <div class="detail-hd"><span>个人行邮税号 「04019900」</span></div>
            <div class="detail-hd">10位HS编码+3位CIQ代码(中国海关申报13位海关编码)</div>
            <table><tr><td class="tdtoth">10位HS编码+3位CIQ代码</td><td class="tdtoth">商品信息</td></tr>
              <tr><td>6108320000.101</td><td>儿童服装</td></tr></table>
            <div class="detail-hd">所属分类及章节、品目</div>
            <table><tr><td>类目</td><td>第十一类 纺织原料及纺织制品</td></tr></table>
            <div class="detail-hd" id="sbsl">申报实例汇总</div>
            <table><tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>{examples}</table>
            "#
        );
        let seed = ApiHsCodeDto {
            code: "6108320000".into(),
            normalized_code: "6108320000".into(),
            detail_url: "https://www.i5a6.com/hscode/detail/6108320000".into(),
            ..Default::default()
        };
        let bundle = detail(&html, &seed, OBSERVED).unwrap();
        assert_eq!(bundle.personal_postal_tax_code, "04019900");
        assert_eq!(bundle.ciq_entries.len(), 1);
        assert_eq!(bundle.classification_entries.len(), 1);
        assert_eq!(bundle.declaration_examples.len(), 20);
        assert_eq!(bundle.item.elements, "织造方法;类别;成分含量");
    }
}
