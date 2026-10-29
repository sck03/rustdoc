//! OA-specific field domain; layout stays in the shared V3 designer/renderer.
use super::*;
use export_doc_domain::designer::{Design, Field, Kind, ReportBlock};
const DEFAULT: &[u8] =
    include_bytes!("../../../../../Templates/Office/approval_sheet_template.dtpl");
const TYPES: &[(&str, &str)] = &[
    ("expense", "费用报销审批单"),
    ("leave", "请假审批单"),
    ("overtime", "加班审批单"),
    ("travel", "出差审批单"),
    ("purchase", "采购申请审批单"),
    ("general:Seal", "用印审批单"),
    ("general:Certificate", "证明申请审批单"),
    ("general:IT", "IT 服务审批单"),
    ("general:Repair", "维修申请审批单"),
    ("general:Other", "通用申请审批单"),
    ("personnel:Hire", "入职审批单"),
    ("personnel:Confirm", "转正审批单"),
    ("personnel:Transfer", "调岗审批单"),
    ("personnel:Depart", "离职审批单"),
    ("personnel:Rehire", "返聘审批单"),
];
pub(super) fn defaults() -> Result<Value> {
    let content = default_content()?;
    Ok(json!(
        TYPES
            .iter()
            .map(|(key, name)| json!({"key":key,"name":name,"content":content}))
            .collect::<Vec<_>>()
    ))
}
pub(super) fn default_content() -> Result<String> {
    let design = export_doc_domain::report_template_format::decode(DEFAULT).map_err(invalid)?;
    Ok(serde_json::to_string(&design)?)
}
pub(super) fn fields() -> Vec<Field> {
    export_doc_contracts::openapi::OA_SHEET_FIELDS
        .iter()
        .chain(export_doc_contracts::openapi::OA_SHEET_DETAIL_FIELDS)
        .map(|(key, label)| Field {
            path: format!("Oa.{key}"),
            label: (*label).into(),
            category: "审批单".into(),
            expression: format!("{{{{ Oa.{key} }}}}"),
        })
        .collect()
}
pub(super) fn catalog() -> Value {
    json!({"reportType":"OaApproval","categoryOrder":["审批单"],"fields":fields().iter().map(|f|json!({"reportType":"OaApproval","label":f.label,"category":f.category,"value":f.expression})).collect::<Vec<_>>()})
}
pub(super) fn validate_design(content: &str) -> Result<Design> {
    if content.len() > 256 * 1024 {
        return Err(invalid("审批单模板不能超过 256 KiB。"));
    }
    let design = Design::from_source(content).map_err(invalid)?;
    if design.report_type != "OaApproval" || !design.resources.is_empty() {
        return Err(invalid("审批单须使用 OA 数据域及随包字体。"));
    }
    export_doc_domain::template::validate(&design, &fields()).map_err(invalid)?;
    // Count only unconditional printed fields, not metadata, hidden components or
    // conditional branches. Unbounded facts must use a paginating body grid.
    let mut printed = std::collections::HashSet::new();
    let mut growing = std::collections::HashSet::new();
    for layer in design.layers.iter().filter(|layer| layer.visible) {
        for element in layer
            .elements
            .iter()
            .filter(|element| element.visible && element.output_enabled)
        {
            match &element.kind {
                Kind::Field { field_path, .. } => {
                    printed.insert(field_path.clone());
                }
                Kind::Flow {
                    block: ReportBlock::Grid(grid),
                    ..
                } if grid.output.as_ref().is_none_or(|output| output.enabled) => {
                    if grid.grow_to_content && (layer.role != "Body" || layer.print.first_page_only)
                    {
                        return Err(invalid("自动增高表格须置于可续页的正文图层。"));
                    }
                    for cell in grid
                        .rows
                        .iter()
                        .flat_map(|row| &row.cells)
                        .filter(|cell| cell.content_kind == "Field")
                    {
                        printed.insert(cell.field_path.clone());
                        if grid.grow_to_content {
                            growing.insert(cell.field_path.clone());
                        }
                    }
                }
                _ => {}
            }
        }
    }
    for field in fields().into_iter().filter(|field| {
        export_doc_contracts::openapi::OA_SHEET_FIELDS
            .iter()
            .any(|(key, _)| field.path == format!("Oa.{key}"))
    }) {
        if field.path == "Oa.Applicant"
            && [
                "Oa.ApplicantName",
                "Oa.DepartmentName",
                "Oa.EmployeeNumber",
                "Oa.JobTitle",
                "Oa.SubmittedAt",
            ]
            .iter()
            .all(|key| growing.contains(*key))
        {
            continue;
        }
        if !printed.contains(&field.path) {
            return Err(invalid(format!(
                "审批单须保留可打印字段：{}。",
                field.label
            )));
        }
        if [
            "Oa.Applicant",
            "Oa.Title",
            "Oa.Summary",
            "Oa.Reason",
            "Oa.Attachments",
            "Oa.Approvals",
            "Oa.ApprovalResult",
        ]
        .contains(&field.path.as_str())
            && !growing.contains(&field.path)
        {
            return Err(invalid(format!(
                "{}须置于正文自动增高表格，以完整输出长内容。",
                field.label
            )));
        }
    }
    Ok(design)
}

pub(super) fn preview(service: &NativeService, actor: &Actor, body: &Value) -> Result<Value> {
    service.store.read(|tx| {
        let current = auth::current_actor_in(tx, actor.id, actor.edition)?;
        if !current.admin {
            return Err(error(403, "只有管理员可以编辑审批单模板。"));
        }
        Ok(())
    })?;
    required(body, "name", "审批单名称", 80)?;
    let design = validate_design(&text(body, "content"))?;
    let data = export_doc_report::ReportData {
        report_type: "OaApproval",
        root: json!({"Oa":{
            "CompanyName":"示例公司","FormName":body["name"],"Reference":"排版样例 · 未审批",
            "ApplicantName":"张三","DepartmentName":"业务部","EmployeeNumber":"A001","JobTitle":"业务专员","SubmittedAt":"2026-01-01 09:00:00 +08:00",
            "ApprovalResult":"排版样例 · 未审批，不作为已批准凭据",
            "Applicant":"示例申请人 · 工号 001 · 示例部门\n提交时间：2026-01-01T09:00:00+08:00",
            "Title":"审批单版式预览","Summary":"示例事项\nCNY 1,000.00 · 人民币壹仟元整",
            "Reason":"此处显示申请说明。正式审批单使用提交时冻结的内容；长说明、附件清单及审批意见自动续页。",
            "Attachments":"1. 报销明细.pdf · 报销单\n2. 电子凭证.pdf · 发票/凭证",
            "Approvals":"节点 1 · 部门负责人审批\n审批人：李四 · 结果：同意\n审批意见：同意，按公司制度办理。\n审批时间：2026-01-01 10:00:00 +08:00\n\n节点 2 · 职能负责人审批\n审批人：王五 · 结果：同意\n审批意见：资料齐全。\n审批时间：2026-01-01 11:00:00 +08:00\n候选审批人：赵六；节点已通过，本人未签署。\n\n节点 3 · 公司领导审批\n审批人：钱七 · 结果：同意\n审批意见：同意，请办理后续手续。\n审批时间：2026-01-01 12:00:00 +08:00",
            "GeneratedAt":"排版样例，不作为已批准凭据"
        }}),
        images: Default::default(),
    };
    let _fonts = service.report_fonts.enter()?;
    let cancelled = crate::operation::cancellation_flag();
    let document = export_doc_report::render_design(&data, &design, &cancelled)?;
    Ok(json!({"html":document.html(&service.report_fonts,&cancelled)?}))
}
pub(super) fn validate(value: &Value) -> Result<()> {
    let templates = value
        .as_array()
        .filter(|v| v.len() == TYPES.len())
        .ok_or_else(|| invalid("请保留全部申请类型的审批单模板。"))?;
    let mut seen = std::collections::HashSet::new();
    for template in templates {
        let key = text(template, "key");
        if !TYPES.iter().any(|(k, _)| *k == key) || !seen.insert(key) {
            return Err(invalid("审批单模板类型无效或重复。"));
        }
        required(template, "name", "审批单名称", 80)?;
        validate_design(&text(template, "content"))?;
    }
    Ok(())
}
pub(super) fn select(settings: &Value, row: &Value) -> Result<Value> {
    let key = match text(row, "kind").as_str() {
        "general" => format!("general:{}", text(row, "category")),
        "personnel" => format!("personnel:{}", text(row, "personnelChange.action")),
        other => other.into(),
    };
    let defaults;
    let templates = if let Some(templates) = settings.get("sheetTemplates") {
        templates
    } else {
        defaults = self::defaults()?;
        &defaults
    };
    let template = templates
        .as_array()
        .and_then(|values| values.iter().find(|v| v["key"] == key))
        .cloned()
        .ok_or_else(|| super::super::error::unavailable("审批单模板缺失。"))?;
    validate_design(&text(&template, "content"))?;
    Ok(template)
}
