use export_doc_contracts::generated_api::ApiHsCodeDto;
use export_doc_domain::hs;
use scraper::{ElementRef, Html, Selector};
use std::collections::BTreeSet;

fn select(value: &str) -> Selector {
    Selector::parse(value).expect("static selector")
}
fn text(element: ElementRef<'_>) -> String {
    element
        .text()
        .collect::<Vec<_>>()
        .join(" ")
        .split_whitespace()
        .collect::<Vec<_>>()
        .join(" ")
}
fn leading(value: &str) -> Option<String> {
    let raw: String = value
        .trim()
        .chars()
        .take_while(|c| c.is_ascii_digit() || *c == '.' || *c == '．')
        .collect();
    hs::code(&raw).ok().filter(|c| c.len() >= 4)
}
fn field(headers: &[String], labels: &[&str]) -> Option<usize> {
    headers
        .iter()
        .position(|h| labels.iter().any(|s| h.contains(s)))
}
pub fn empty_result(html: &str) -> bool {
    let document = Html::parse_document(html);
    let text = text(document.root_element());
    ["0 条", "0条", "未找到", "无相关"]
        .iter()
        .any(|s| text.contains(s))
}
pub fn search(html: &str, observed: &str) -> Result<Vec<ApiHsCodeDto>, String> {
    let document = Html::parse_document(html);
    let mut result = vec![];
    let mut seen = BTreeSet::new();
    for table in document.select(&select("table")) {
        let rows: Vec<_> = table.select(&select("tr")).collect();
        let header = rows.iter().enumerate().take(8).find_map(|(i, row)| {
            let headers: Vec<_> = row.select(&select("th,td")).map(text).collect();
            Some((
                i,
                field(&headers, &["编码", "税号"])?,
                field(&headers, &["名称", "品名"])?,
                headers,
            ))
        });
        let Some((start, code_column, name_column, headers)) = header else {
            continue;
        };
        let specification = field(&headers, &["规格型号", "申报要素", "规格", "型号"]);
        let example_count = field(&headers, &["实例汇总", "实例数量", "案例数量"]);
        let declaration = specification.is_some() && example_count.is_none();
        for row in rows.into_iter().skip(start + 1) {
            if result.len() >= 2000 {
                break;
            }
            let cells: Vec<_> = row.select(&select("td")).collect();
            let read = |index: Option<usize>| {
                index
                    .and_then(|i| cells.get(i))
                    .copied()
                    .map(text)
                    .unwrap_or_default()
            };
            let raw = read(Some(code_column));
            let Some(code) = leading(&raw) else {
                continue;
            };
            let name = read(Some(name_column));
            if name.is_empty() {
                continue;
            }
            let elements = read(specification);
            let key = (code.clone(), name.clone(), elements.clone(), declaration);
            if !seen.insert(key) {
                continue;
            }
            let url = row
                .select(&select("a[href]"))
                .filter_map(|link| link.value().attr("href"))
                .filter_map(|link| crate::trusted_url(link).ok())
                .find(|url| url.path().starts_with("/hscode/detail/"));
            let mut item = ApiHsCodeDto {
                code: code.clone(),
                normalized_code: code,
                name,
                elements,
                detail_url: url.map(|u| u.into()).unwrap_or_default(),
                source_name: "i5a6（第三方参考）".into(),
                status: if raw.contains("已作废") || text(row).contains("已作废") {
                    "Obsolete"
                } else {
                    "ReferenceOnly"
                }
                .into(),
                remote_record_kind: if declaration {
                    "DeclarationExample"
                } else {
                    "StandardCode"
                }
                .into(),
                update_time: Some(observed.into()),
                observed_at: Some(observed.into()),
                ..Default::default()
            };
            item.unit = read(field(&headers, &["单位"]));
            item.rebate_rate = read(field(&headers, &["退税"]));
            item.instance_count = example_count.and_then(|i| {
                read(Some(i))
                    .chars()
                    .filter(|c| c.is_ascii_digit())
                    .collect::<String>()
                    .parse()
                    .ok()
            });
            item.summary_url = if item.detail_url.is_empty() {
                String::new()
            } else {
                format!("{}#sbsl", item.detail_url.split('#').next().unwrap_or(""))
            };
            item.evidence_url = item.detail_url.clone();
            result.push(item);
        }
    }
    if result.is_empty() {
        for link in document.select(&select("a[href]")) {
            let Some(url) = link
                .value()
                .attr("href")
                .and_then(|u| crate::trusted_url(u).ok())
                .filter(|u| u.path().starts_with("/hscode/detail/"))
            else {
                continue;
            };
            let label = text(link);
            let Some(code) = leading(&label) else {
                continue;
            };
            let name = label
                .trim_start_matches(|c: char| {
                    c.is_ascii_digit() || c == '.' || c == '．' || c.is_whitespace()
                })
                .to_string();
            if name.is_empty() {
                continue;
            }
            if !seen.insert((code.clone(), name.clone(), String::new(), false)) {
                continue;
            }
            result.push(ApiHsCodeDto {
                code: code.clone(),
                normalized_code: code,
                name,
                detail_url: url.into(),
                source_name: "i5a6（第三方参考）".into(),
                status: "ReferenceOnly".into(),
                remote_record_kind: "StandardCode".into(),
                observed_at: Some(observed.into()),
                ..Default::default()
            });
        }
    }
    Ok(result)
}
pub fn detail(html: &str, seed: &ApiHsCodeDto, observed: &str) -> Result<ApiHsCodeDto, String> {
    let document = Html::parse_document(html);
    let root = document
        .select(&select("#hscode-detail"))
        .next()
        .or_else(|| {
            document.select(&select("table")).max_by_key(|table| {
                let content = text(*table);
                ["商品编码", "商品名称", "申报要素", "法定第一单位"]
                    .iter()
                    .filter(|s| content.contains(**s))
                    .count()
            })
        })
        .unwrap_or_else(|| document.root_element());
    let mut fields = std::collections::BTreeMap::new();
    for row in root.select(&select("tr")) {
        let cells: Vec<_> = row.select(&select("th,td")).map(text).collect();
        for pair in cells.chunks_exact(2) {
            fields.insert(
                pair[0].trim_end_matches([':', '：']).to_string(),
                pair[1].clone(),
            );
        }
    }
    let read = |labels: &[&str]| {
        labels
            .iter()
            .find_map(|key| fields.get(*key))
            .cloned()
            .unwrap_or_default()
    };
    let code = read(&["商品编码", "HS编码"]);
    let name = read(&["商品名称", "品名"]);
    if code.is_empty() && name.is_empty() {
        return Err("详情页面格式变化或需要交互验证，未读取到编码资料。".into());
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
    item.status = if text(root).contains("已作废") {
        "Obsolete"
    } else {
        "ReferenceOnly"
    }
    .into();
    item.source_name = "i5a6（第三方参考）".into();
    item.last_verified_at = Some(observed.into());
    item.observed_at = Some(observed.into());
    item.update_time = Some(observed.into());
    item.recommended_keywords = Some(
        document
            .select(&select("a[href]"))
            .filter_map(|a| a.value().attr("href"))
            .filter_map(|href| crate::trusted_url(href).ok())
            .filter_map(|url| url.path().strip_prefix("/hscode/key/").and_then(leading))
            .collect::<BTreeSet<_>>()
            .into_iter()
            .collect(),
    );
    Ok(item)
}
