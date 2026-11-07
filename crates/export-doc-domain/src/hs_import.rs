//! Column recognition and regulatory unit names; no file or database access.
use crate::hs;
use std::{collections::BTreeMap, sync::OnceLock};
pub const COLUMNS: &[(&str, &[&str])] = &[
    (
        "code",
        &[
            "HS编码",
            "HSCODE",
            "海关编码",
            "商品编码",
            "商品税号",
            "税则号列",
            "税则号",
            "税号",
        ],
    ),
    (
        "name",
        &[
            "商品名称",
            "货品名称",
            "中文品名",
            "税目名称",
            "商品品名",
            "名称",
            "品名",
            "NAME",
        ],
    ),
    (
        "unit",
        &[
            "法定第一单位",
            "第一法定单位",
            "法一单位",
            "计量单位1",
            "第一单位",
            "单位",
            "UNIT1",
            "UNIT",
        ],
    ),
    (
        "unit2",
        &[
            "法定第二单位",
            "第二法定单位",
            "法二单位",
            "计量单位2",
            "第二单位",
            "UNIT2",
        ],
    ),
    (
        "rebateRate",
        &[
            "出口退税率",
            "出口商品退税率",
            "退税率",
            "REBATERATE",
            "REBATE",
        ],
    ),
    (
        "normalTariffRate",
        &[
            "普通税率",
            "普通关税率",
            "普通进口税率",
            "GENERALTARIFFRATE",
        ],
    ),
    (
        "preferentialTariffRate",
        &[
            "优惠税率",
            "最惠国税率",
            "最惠国税率MFN",
            "MFN税率",
            "PREFERENTIALTARIFFRATE",
        ],
    ),
    (
        "exportTariffRate",
        &["出口税率", "出口关税率", "EXPORTTARIFFRATE"],
    ),
    ("consumptionTaxRate", &["消费税率", "CONSUMPTIONTAXRATE"]),
    (
        "valueAddedTaxRate",
        &["增值税率", "进口增值税率", "VAT", "VAT率"],
    ),
    (
        "supervisionConditions",
        &[
            "海关监管条件",
            "监管证件代码",
            "许可证代码",
            "监管条件",
            "SUPERVISIONCONDITIONS",
            "SUPERVISION",
        ],
    ),
    (
        "inspectionCategory",
        &[
            "检验检疫类别",
            "检疫类别",
            "检验检疫",
            "CIQ类别",
            "INSPECTIONCATEGORY",
            "INSPECTION",
        ],
    ),
    (
        "elements",
        &[
            "规范申报要素",
            "申报要素内容",
            "申报要素",
            "规格型号",
            "ELEMENTS",
            "ELEMENT",
        ],
    ),
    (
        "description",
        &[
            "英文名称",
            "英文品名",
            "商品描述",
            "英文描述",
            "DESCRIPTION",
            "DESC",
        ],
    ),
    ("notes", &["备注", "说明", "REMARKS", "NOTES"]),
];
pub fn headers(rows: &[Vec<String>]) -> Result<(usize, Vec<(&'static str, usize)>), String> {
    let mut best = None;
    for (index, row) in rows.iter().take(30).enumerate() {
        let mapping: Vec<_> = COLUMNS
            .iter()
            .filter_map(|(field, aliases)| {
                row.iter()
                    .position(|v| aliases.iter().any(|alias| hs::text(v) == hs::text(alias)))
                    .map(|column| (*field, column))
            })
            .collect();
        if mapping.iter().any(|(f, _)| *f == "code")
            && mapping.iter().any(|(f, _)| *f == "name")
            && best
                .as_ref()
                .is_none_or(|(_, previous): &(usize, Vec<(&str, usize)>)| {
                    mapping.len() > previous.len()
                })
        {
            best = Some((index, mapping));
        }
    }
    best.ok_or_else(|| "未找到包含 HS 编码和商品名称的表头。".into())
}
pub fn unit(value: &str) -> String {
    // Official CusUnit snapshot and provenance: docs/海关计量单位数据来源.md.
    static NAMES: OnceLock<BTreeMap<u16, &str>> = OnceLock::new();
    let names = NAMES.get_or_init(|| {
        include_str!("../resources/customs-units.tsv")
            .lines()
            .skip(1)
            .map(|line| {
                let (code, name) = line.split_once('\t').expect("bundled customs unit row");
                (code.parse().expect("bundled customs unit code"), name)
            })
            .collect()
    });
    let value = value.trim();
    let name = value
        .bytes()
        .all(|byte| byte.is_ascii_digit())
        .then(|| value.parse::<u16>().ok().and_then(|code| names.get(&code)))
        .flatten();
    name.copied().unwrap_or(value).into()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn official_customs_units_replace_incorrect_codes_and_preserve_unknown_input() {
        for (code, expected) in [
            ("013", "枝"),
            ("30", "米"),
            ("070", "吨"),
            ("132", "本"),
            ("149", "百万贝可"),
            ("163", "部"),
            ("181", "百万英热"),
            (" 00035 ", "千克"),
        ] {
            assert_eq!(unit(code), expected, "{code}");
        }
        for unknown in [
            "",
            "137",
            "138",
            "999",
            "0999",
            "+035",
            "-35",
            "35.0",
            "千克",
            "自定义单位",
        ] {
            assert_eq!(unit(unknown), unknown);
        }
        let rows: Vec<_> = include_str!("../resources/customs-units.tsv")
            .lines()
            .skip(1)
            .map(|line| line.split_once('\t').unwrap())
            .collect();
        assert_eq!(rows.len(), 127);
        assert!(rows.windows(2).all(|pair| pair[0].0 < pair[1].0));
        for (code, name) in rows {
            assert!(code.len() == 3 && code.bytes().all(|b| b.is_ascii_digit()));
            assert!(!name.is_empty());
            assert_eq!(unit(code), name);
        }
    }
}
