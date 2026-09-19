//! HS normalization, evidence validity and attribute-aware matching.
use crate::generated_api::ApiHsCodeDto;
use sha2::{Digest, Sha256};
use std::collections::BTreeSet;
use unicode_normalization::UnicodeNormalization;

pub fn code(value: &str) -> Result<String, String> {
    let code: String = value
        .nfkc()
        .filter(|ch| ch.is_alphanumeric())
        .flat_map(char::to_uppercase)
        .collect();
    if code.is_empty() || code.len() > 20 || !code.bytes().all(|ch| ch.is_ascii_digit()) {
        return Err("HS 编码须为 1 至 20 位数字。".into());
    }
    Ok(code)
}
pub fn trusted(row: &ApiHsCodeDto) -> bool {
    row.status.eq_ignore_ascii_case("Active")
        && !row.source_name.trim().is_empty()
        && row
            .effective_year
            .is_some_and(|year| (2000..=2100).contains(&year))
        && row
            .last_verified_at
            .as_deref()
            .is_some_and(|date| !date.is_empty())
}
pub fn normalize(row: &mut ApiHsCodeDto) -> Result<(), String> {
    row.code = code(&row.code)?;
    row.normalized_code = row.code.clone();
    for (value, label, maximum, required) in [
        (&mut row.name, "商品名称", 200, true),
        (&mut row.unit, "单位", 50, false),
        (&mut row.description, "描述", 500, false),
        (&mut row.elements, "申报要素", 500, false),
        (&mut row.source_name, "来源", 200, false),
        (&mut row.notes, "备注", 1000, false),
        (&mut row.rebate_rate, "退税率", 100, false),
        (&mut row.replaced_by_codes, "替代编码", 500, false),
        (&mut row.supervision_conditions, "监管条件", 500, false),
        (&mut row.inspection_category, "检验检疫类别", 500, false),
    ] {
        *value = crate::crm::text(value, label, maximum, required)?;
    }
    row.status = match row.status.to_lowercase().as_str() {
        "active" => "Active",
        "obsolete" => "Obsolete",
        "suspectedobsolete" => "SuspectedObsolete",
        "" | "referenceonly" => "ReferenceOnly",
        _ => return Err("HS 有效状态无效。".into()),
    }
    .into();
    if row
        .effective_year
        .is_some_and(|year| !(2000..=2100).contains(&year))
    {
        return Err("适用年度须为 2000 至 2100。".into());
    }
    if row.status == "Active" && !trusted(row) {
        return Err("当前有效编码须包含可信来源、适用年度和验证时间。".into());
    }
    row.extra.clear();
    Ok(())
}
pub fn fingerprint(values: &[&str]) -> String {
    let value = values
        .iter()
        .map(|s| s.trim().to_uppercase())
        .collect::<Vec<_>>()
        .join("|");
    Sha256::digest(value.as_bytes())
        .iter()
        .map(|byte| format!("{byte:02X}"))
        .collect()
}
pub fn text(value: &str) -> String {
    const SYNONYMS: &[(&str, &str)] = &[
        ("T-SHIRT", "T恤衫"),
        ("WOMEN'S", "女式"),
        ("KNITTED", "针织"),
        ("TSHIRT", "T恤衫"),
        ("WOMENS", "女式"),
        ("COTTON", "棉"),
        ("MEN'S", "男式"),
        ("MENS", "男式"),
        ("针织物", "针织"),
        ("T恤", "T恤衫"),
        ("男士", "男式"),
        ("男款", "男式"),
        ("女士", "女式"),
        ("女款", "女式"),
        ("全棉", "100%棉"),
        ("纯棉", "100%棉"),
    ];
    let normalized = value.nfkc().collect::<String>().trim().to_uppercase();
    let mut source = normalized.as_str();
    let mut output = String::new();
    while !source.is_empty() {
        if let Some((from, to)) = SYNONYMS.iter().find(|(from, to)| {
            source.starts_with(from) && !(to.starts_with(from) && source.starts_with(to))
        }) {
            output.push_str(to);
            source = &source[from.len()..];
        } else {
            let ch = source.chars().next().expect("nonempty");
            if ch.is_alphanumeric() || ch == '%' {
                output.push(ch);
            }
            source = &source[ch.len_utf8()..];
        }
    }
    output
}
pub fn grams(text: &str) -> BTreeSet<String> {
    let chars: Vec<_> = text.chars().collect();
    if chars.len() <= 2 {
        return if chars.is_empty() {
            BTreeSet::new()
        } else {
            BTreeSet::from([text.into()])
        };
    }
    [2, 3]
        .into_iter()
        .flat_map(|n| chars.windows(n).map(|s| s.iter().collect::<String>()))
        .collect()
}
pub fn score(query: &str, candidate: &str) -> i64 {
    if query.is_empty() || candidate.is_empty() {
        return 0;
    }
    if query == candidate {
        return 90;
    }
    let exact = if candidate.contains(query) {
        72
    } else if query.contains(candidate) {
        62
    } else {
        0
    };
    let left = grams(query);
    let right = grams(candidate);
    let denominator = left.len() + right.len();
    let dice = if denominator == 0 {
        0
    } else {
        ((left.intersection(&right).count() * 120) as f64 / denominator as f64).round_ties_even()
            as i64
    };
    let related = if (query.contains("针织") && candidate.contains("钩编"))
        || (query.contains("钩编") && candidate.contains("针织"))
    {
        12
    } else {
        0
    };
    (exact.max(dice) + related).min(90)
}
pub struct Assessment {
    pub penalty: i64,
    pub reasons: Vec<String>,
    pub warnings: Vec<String>,
}
pub fn attributes(query: &str, candidate: &str) -> Assessment {
    let mut result = Assessment {
        penalty: 0,
        reasons: vec![],
        warnings: vec![],
    };
    for (label, penalty, compatible, groups) in [
        (
            "性别",
            38,
            false,
            vec![vec!["男式", "男童"], vec!["女式", "女童"]],
        ),
        (
            "织造方式",
            24,
            false,
            vec![vec!["针织", "钩编"], vec!["梭织", "机织"]],
        ),
        (
            "材质",
            28,
            true,
            vec![
                vec!["涤纶", "聚酯", "化纤", "粘胶", "氨纶", "锦纶"],
                vec!["棉"],
                vec!["丝", "真丝"],
                vec!["毛", "羊毛"],
                vec!["麻"],
            ],
        ),
        (
            "品类",
            32,
            false,
            vec![
                vec!["T恤衫", "T恤"],
                vec!["睡衣", "睡裙"],
                vec!["衬衫"],
                vec!["连衣裙"],
                vec!["夹克"],
                vec!["长裤", "短裤", "西裤", "裤子"],
            ],
        ),
    ] {
        let find = |value: &str| -> Vec<usize> {
            let value = if compatible {
                value.replace("人棉", "粘胶")
            } else {
                value.into()
            };
            groups
                .iter()
                .enumerate()
                .filter(|(_, group)| group.iter().any(|s| value.contains(s)))
                .map(|(i, _)| i)
                .take(if compatible { usize::MAX } else { 1 })
                .collect()
        };
        let left = find(query);
        let right = find(candidate);
        let Some(&first) = left.first() else {
            continue;
        };
        if right.is_empty() {
            result.reasons.push(format!(
                "{label}：查询为{}，候选未明确限定",
                groups[first][0]
            ));
        } else if let Some(&same) = left.iter().find(|i| right.contains(i)) {
            result
                .reasons
                .push(format!("{label}：{}一致", groups[same][0]));
        } else {
            result.penalty += penalty;
            result.warnings.push(format!(
                "{label}冲突：查询为{}，候选为{}",
                groups[first][0], groups[right[0]][0]
            ));
        }
    }
    result.penalty = result.penalty.min(90);
    if result.reasons.is_empty() && result.warnings.is_empty() {
        result.reasons.push("未检测到明显属性冲突".into());
    }
    result
}
