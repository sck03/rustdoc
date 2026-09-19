use crate::{
    FormSection,
    form_model::FormModel,
    form_sections::{self, DisclosureState, Section},
    model,
};
use export_doc_engine::{contracts, generated_api::*};
use serde_json::{Value, json};
use slint::Model;
use std::collections::{BTreeMap, BTreeSet};
#[derive(Default)]
pub struct HsModel {
    pub rows: Vec<Value>,
    pub selected: usize,
    pub checked: BTreeSet<i64>,
    pub form: Option<FormModel>,
    pub editing_code: bool,
    pub preview: Option<Value>,
}
pub fn columns(tab: i32) -> &'static [&'static str] {
    match tab {
        0 => &[
            "当前 / 原始编码",
            "商品名称",
            "匹配分",
            "核实状态",
            "本地案例",
        ],
        1 => &["编码", "商品名称", "法定单位", "有效状态", "年度 / 来源"],
        2 => &["原始编码", "当前编码", "商品名称", "来源", "人工核实"],
        3 => &["原始编码", "当前编码", "商品名称", "历史条数", "核实状态"],
        4 => &["原始编码", "建议编码", "商品名称", "出现次数", "审核状态"],
        5 => &["文件行号", "变更类型", "编码", "商品名称", "预检说明"],
        _ => &["编码", "商品名称", "参考类型", "参考状态", "来源"],
    }
}
pub fn display(value: &Value) -> String {
    if let Some(values) = value.as_array() {
        return values
            .iter()
            .map(display)
            .filter(|s| !s.is_empty())
            .collect::<Vec<_>>()
            .join("；");
    }
    match value.as_str() {
        Some("Active") => "当前有效".into(),
        Some("ReferenceOnly") => "仅供参考".into(),
        Some("SuspectedObsolete") => "疑似作废".into(),
        Some("Obsolete") => "已作废".into(),
        Some("Unresolved" | "ObsoleteUnresolved") => "待核实".into(),
        Some("ObsoleteMapped") => "已映射替代".into(),
        Some("ManuallyVerified") => "人工确认".into(),
        Some("SuggestedReplacement") => "建议替代".into(),
        Some("Ambiguous") => "存在多个替代".into(),
        Some("Pending") => "待审核".into(),
        Some("Confirmed") => "已确认".into(),
        Some("Ignored") => "已忽略".into(),
        Some("Add") => "新增".into(),
        Some("Update") => "更新".into(),
        Some("Unchanged") => "未变".into(),
        Some("Invalid") => "无效".into(),
        Some("Conflict") => "冲突".into(),
        Some("StandardCode") => "税则参考".into(),
        Some("DeclarationExample") => "申报参考".into(),
        Some(s) => s.into(),
        None => {
            if let Some(b) = value.as_bool() {
                if b { "是" } else { "否" }.into()
            } else if value.is_number() {
                value.to_string()
            } else {
                String::new()
            }
        }
    }
}
impl HsModel {
    pub fn data_rows(&self, tab: i32) -> Vec<crate::DataRow> {
        self.rows
            .iter()
            .enumerate()
            .map(|(index, row)| {
                let fields = match tab {
                    0 => vec![
                        if text(row, "currentCode").is_empty() {
                            display(&row["rawCode"])
                        } else {
                            display(&row["currentCode"])
                        },
                        display(&row["name"]),
                        display(&row["score"]),
                        display(&row["resolutionStatus"]),
                        display(&row["exampleCount"]),
                    ],
                    1 => vec![
                        display(&row["code"]),
                        display(&row["name"]),
                        display(&row["unit"]),
                        display(&row["status"]),
                        format!(
                            "{} {}",
                            display(&row["effectiveYear"]),
                            display(&row["sourceName"])
                        ),
                    ],
                    2 => [
                        "rawReportedHsCode",
                        "resolvedCurrentHsCode",
                        "productName",
                        "source",
                        "isManuallyVerified",
                    ]
                    .iter()
                    .map(|k| display(&row[*k]))
                    .collect(),
                    3 => [
                        "rawCode",
                        "currentCode",
                        "productName",
                        "sourceCount",
                        "resolutionStatus",
                    ]
                    .iter()
                    .map(|k| display(&row[*k]))
                    .collect(),
                    4 => [
                        "rawReportedHsCode",
                        "suggestedCurrentHsCode",
                        "productName",
                        "seenCount",
                        "reviewStatus",
                    ]
                    .iter()
                    .map(|k| display(&row[*k]))
                    .collect(),
                    5 => vec![
                        display(&row["rowNumber"]),
                        display(&row["changeType"]),
                        display(&row["item"]["code"]),
                        display(&row["item"]["name"]),
                        display(&row["message"]),
                    ],
                    _ => ["code", "name", "remoteRecordKind", "status", "sourceName"]
                        .iter()
                        .map(|k| display(&row[*k]))
                        .collect(),
                };
                crate::DataRow {
                    id: index as i32 + 1,
                    cells: model(
                        fields
                            .into_iter()
                            .enumerate()
                            .map(|(i, s)| {
                                if i == 0
                                    && row["id"]
                                        .as_i64()
                                        .is_some_and(|id| self.checked.contains(&id))
                                {
                                    format!("☑ {s}").into()
                                } else {
                                    s.into()
                                }
                            })
                            .collect(),
                    ),
                }
            })
            .collect()
    }
}
pub fn details(row: &Value, tab: i32) -> String {
    let entries = &[
        ("code", "编码"),
        ("name", "商品名称"),
        ("rawCode", "原始编码"),
        ("currentCode", "当前编码"),
        ("rawReportedHsCode", "原始编码"),
        ("resolvedCurrentHsCode", "当前编码"),
        ("suggestedCurrentHsCode", "建议编码"),
        ("productName", "商品名称"),
        ("standardName", "税则品名"),
        ("specification", "规格与申报要素"),
        ("elements", "申报要素"),
        ("description", "描述"),
        ("unit", "法定单位"),
        ("status", "有效状态"),
        ("resolutionStatus", "核实状态"),
        ("reviewStatus", "审核状态"),
        ("source", "来源"),
        ("sourceName", "来源"),
        ("standardSource", "税则来源"),
        ("effectiveYear", "年度"),
        ("sourceYear", "来源年度"),
        ("lastVerifiedAt", "验证时间"),
        ("observedAt", "读取时间"),
        ("sourceUrl", "来源地址"),
        ("evidenceUrl", "证据地址"),
        ("detailUrl", "参考详情地址"),
        ("replacementCandidates", "替代候选"),
        ("replacedByCodes", "替代编码"),
        ("matchReasons", "匹配依据"),
        ("conflictWarnings", "属性冲突"),
        ("rebateRate", "出口退税率"),
        ("normalTariffRate", "普通税率"),
        ("preferentialTariffRate", "优惠税率"),
        ("valueAddedTaxRate", "增值税率"),
        ("supervisionConditions", "监管条件"),
        ("inspectionCategory", "检验检疫类别"),
        ("notes", "备注"),
        ("changeType", "变更类型"),
        ("changedFields", "变更字段"),
        ("message", "说明"),
    ];
    let mut lines = entries
        .iter()
        .filter_map(|(key, label)| {
            let value = display(&row[*key]);
            (!value.is_empty()).then(|| format!("{label}：{value}"))
        })
        .collect::<Vec<_>>();
    if tab == 5 {
        lines.push(details(&row["item"], 1));
    }
    lines.join("\n")
}
pub fn text(row: &Value, key: &str) -> String {
    row[key].as_str().map(str::to_owned).unwrap_or_else(|| {
        if row[key].is_null() {
            String::new()
        } else {
            row[key].to_string()
        }
    })
}
impl HsModel {
    pub fn dirty(&self) -> bool {
        self.form
            .as_ref()
            .is_some_and(|f| f.value != f.baseline || !f.error.is_empty())
    }
    pub fn row(&self) -> Option<&Value> {
        self.rows.get(self.selected)
    }
    pub fn open(&mut self, row: Option<Value>, code: bool) {
        self.editing_code = code;
        let schema = contracts::request(if code {
            CREATE_HS_CODE.id
        } else {
            SAVE_HS_CODE_KNOWLEDGE_EXAMPLE.id
        });
        let value=row.unwrap_or_else(||if code{let mut row=contracts::initial(schema);row["status"]=json!("ReferenceOnly");row}else{json!({"id":0,"rawReportedHsCode":"","resolvedCurrentHsCode":"","productName":"","specification":"","source":"Manual","sourceYear":null,"resolutionStatus":"Unresolved","isManuallyVerified":false})});
        self.form = Some(FormModel::new(
            schema,
            value,
            BTreeMap::from([
                (
                    "status".into(),
                    [
                        ("ReferenceOnly", "仅供参考"),
                        ("Active", "当前有效"),
                        ("SuspectedObsolete", "疑似作废"),
                        ("Obsolete", "已作废"),
                    ]
                    .map(|(k, v)| (json!(k), v.into()))
                    .to_vec(),
                ),
                (
                    "resolutionStatus".into(),
                    [
                        ("Unresolved", "待核实"),
                        ("Active", "当前有效"),
                        ("ObsoleteMapped", "已映射替代"),
                        ("ManuallyVerified", "人工确认"),
                    ]
                    .map(|(k, v)| (json!(k), v.into()))
                    .to_vec(),
                ),
            ]),
        ));
    }
    pub fn sections(&self, state: &mut DisclosureState) -> Vec<FormSection> {
        let Some(form) = &self.form else {
            return vec![];
        };
        let definitions = if self.editing_code {
            vec![
                Section::new(
                    "hs-code-main",
                    "编码与品名",
                    &["code", "name", "unit", "status"],
                    true,
                ),
                Section::new(
                    "hs-code-elements",
                    "申报要素与税率",
                    &[
                        "elements",
                        "description",
                        "rebateRate",
                        "supervisionConditions",
                        "inspectionCategory",
                        "normalTariffRate",
                        "preferentialTariffRate",
                        "exportTariffRate",
                        "consumptionTaxRate",
                        "valueAddedTaxRate",
                    ],
                    true,
                ),
                Section::new(
                    "hs-code-evidence",
                    "来源与验证",
                    &[
                        "sourceName",
                        "effectiveYear",
                        "lastVerifiedAt",
                        "replacedByCodes",
                        "notes",
                        "detailUrl",
                    ],
                    false,
                ),
            ]
        } else {
            vec![Section::new(
                "hs-example",
                "申报案例",
                &[
                    "rawReportedHsCode",
                    "resolvedCurrentHsCode",
                    "productName",
                    "specification",
                    "source",
                    "sourceYear",
                    "resolutionStatus",
                    "isManuallyVerified",
                ],
                true,
            )]
        };
        let mut sections = form_sections::build(form, &definitions, state);
        for section in &mut sections {
            section.fields = model(
                section
                    .fields
                    .iter()
                    .map(|mut f| {
                        f.label = label(&f.key).unwrap_or(f.label.as_str()).into();
                        if ["specification", "elements", "description", "notes"]
                            .contains(&f.key.as_str())
                        {
                            f.kind = "multiline".into();
                        }
                        f
                    })
                    .collect(),
            );
        }
        sections
    }
}
pub fn label(key: &str) -> Option<&'static str> {
    Some(match key {
        "code" => "HS 编码",
        "name" => "商品名称",
        "unit" => "法定单位",
        "status" => "有效状态",
        "elements" => "申报要素",
        "description" => "英文名称 / 描述",
        "rebateRate" => "出口退税率",
        "supervisionConditions" => "监管条件",
        "inspectionCategory" => "检验检疫类别",
        "normalTariffRate" => "普通税率",
        "preferentialTariffRate" => "优惠税率",
        "exportTariffRate" => "出口税率",
        "consumptionTaxRate" => "消费税率",
        "valueAddedTaxRate" => "增值税率",
        "sourceName" => "可信来源",
        "effectiveYear" => "适用年度",
        "lastVerifiedAt" => "验证时间（含时区）",
        "replacedByCodes" => "替代编码",
        "notes" => "备注",
        "detailUrl" => "详情地址",
        "rawReportedHsCode" => "历史 / 原始编码",
        "resolvedCurrentHsCode" => "当前有效编码",
        "productName" => "商品名称",
        "specification" => "规格与申报要素",
        "source" => "案例来源",
        "sourceYear" => "来源年度",
        "resolutionStatus" => "核实状态",
        "isManuallyVerified" => "已经人工核实",
        _ => return None,
    })
}
