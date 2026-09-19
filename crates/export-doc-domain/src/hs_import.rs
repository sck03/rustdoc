//! Column recognition and regulatory unit names; no file or database access.
use crate::hs;
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
    let names = &[
        ("001", "台"),
        ("002", "座"),
        ("003", "辆"),
        ("004", "艘"),
        ("005", "架"),
        ("006", "套"),
        ("007", "个"),
        ("008", "只"),
        ("009", "头"),
        ("010", "张"),
        ("011", "件"),
        ("012", "支"),
        ("013", "根"),
        ("014", "条"),
        ("015", "把"),
        ("016", "块"),
        ("017", "卷"),
        ("018", "副"),
        ("019", "枚"),
        ("020", "吊"),
        ("021", "双"),
        ("022", "对"),
        ("023", "箱"),
        ("025", "桶"),
        ("026", "扎"),
        ("027", "包"),
        ("028", "筐"),
        ("029", "罗"),
        ("030", "匹"),
        ("031", "册"),
        ("032", "本"),
        ("033", "格"),
        ("034", "筒"),
        ("035", "千克"),
        ("036", "克"),
        ("037", "毫克"),
        ("038", "吨"),
        ("039", "公担"),
        ("044", "厘米"),
        ("045", "毫米"),
        ("046", "米"),
        ("047", "千米"),
        ("048", "英尺"),
        ("049", "英寸"),
        ("050", "码"),
        ("063", "千瓦时"),
        ("070", "升"),
        ("071", "毫升"),
        ("072", "微升"),
        ("095", "升"),
        ("096", "毫升"),
        ("097", "微升"),
        ("110", "平方米"),
        ("111", "平方英尺"),
        ("112", "平方码"),
        ("115", "立方米"),
        ("116", "立方英尺"),
        ("120", "立方厘米"),
        ("121", "立方毫米"),
        ("126", "立方米"),
        ("132", "升"),
        ("133", "毫升"),
        ("134", "微升"),
        ("135", "升"),
        ("136", "毫升"),
        ("137", "微升"),
        ("138", "升"),
        ("139", "毫升"),
        ("140", "微升"),
        ("141", "升"),
        ("142", "毫升"),
        ("143", "微升"),
        ("144", "升"),
        ("145", "毫升"),
        ("146", "微升"),
        ("147", "升"),
        ("148", "毫升"),
        ("149", "微升"),
        ("163", "克拉"),
    ];
    let normalized = value
        .trim()
        .parse::<u16>()
        .ok()
        .map(|n| format!("{n:03}"))
        .unwrap_or_else(|| value.trim().into());
    names
        .iter()
        .find(|(code, _)| *code == normalized)
        .map(|(_, name)| (*name).into())
        .unwrap_or_else(|| value.trim().into())
}
