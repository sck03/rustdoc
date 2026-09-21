mod detail;
pub use detail::detail;
#[cfg(test)]
mod tests;

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
    // Inline search highlights must not insert spaces into names or specs.
    normalize_text(&element.text().collect::<String>())
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
    let mut result = Vec::new();
    let mut seen = BTreeSet::new();
    let mut append = |value: &str| {
        for digits in value
            .split(|c: char| !c.is_ascii_digit())
            .filter(|v| v.len() >= 4)
        {
            if seen.insert(digits.to_string()) {
                result.push(digits.to_string());
            }
        }
    };
    for link in document.select(&select("a[href]")) {
        if let Some(code) = link
            .value()
            .attr("href")
            .and_then(|href| crate::trusted_url(href).ok())
            .and_then(|url| url.path().strip_prefix("/hscode/key/").and_then(leading))
        {
            append(&code);
            append(&text(link));
        }
    }
    let content = text(document.root_element());
    for (offset, _) in content
        .match_indices("推荐查询")
        .chain(content.match_indices("或者"))
    {
        let suffix = &content[offset..];
        let suffix = suffix
            .strip_prefix("推荐查询")
            .or_else(|| suffix.strip_prefix("或者"))
            .unwrap();
        let suffix =
            suffix.trim_start_matches(|c: char| c.is_whitespace() || c == ':' || c == '：');
        let code: String = suffix.chars().take_while(char::is_ascii_digit).collect();
        append(&code);
    }
    result
}
fn source_name(kind: RemoteRecordKind) -> &'static str {
    match kind {
        RemoteRecordKind::StandardCode => "i5a6（第三方参考）",
        RemoteRecordKind::DeclarationExample => "i5a6（第三方申报实例）",
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
    let mut seen = BTreeSet::new();
    replacements.retain(|item| seen.insert(item.old_code.clone()));
    Ok(SearchBundle {
        records,
        replacements,
    })
}
