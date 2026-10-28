use export_doc_domain::report_template_format;
use export_doc_report::{Fonts, approval, pdf_document, render_design};
use serde_json::json;
use std::{path::PathBuf, sync::atomic::AtomicBool};

#[test]
fn approval_sheet_is_one_page_and_preserves_long_content_in_order() {
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../..");
    let fonts = Fonts::new(&root.join("Resources/Fonts/OpenSource/NotoSansCJKsc-Regular.otf"));
    let _scope = fonts.enter().unwrap();
    let design = report_template_format::decode(include_bytes!(
        "../../../Templates/Office/approval_sheet_template.dtpl"
    ))
    .unwrap();
    let encoded = serde_json::to_string(&design).unwrap();
    assert!(encoded.contains("backgroundColor"));
    assert!(!encoded.contains("电子签章"));
    let round_trip = export_doc_domain::designer::Design::from_source(&encoded).unwrap();
    assert_eq!(design, round_trip);
    let mut snapshot = json!({"requestId":42,"submissionNumber":2,"companyName":"示例贸易有限公司","departmentName":"业务部","submittedAt":"2026-10-28T09:00:00+08:00","formName":"费用报销审批单","request":{
        "kind":"expense","title":"差旅费报销","employeeName":"张三","employeeNumber":"A001","currency":"CNY","totalAmount":"1234.56","reason":"出差费用，明细见附件。","attachments":[{"fileName":"报销明细.pdf","purpose":"ReimbursementForm"},{"fileName":"原始凭证.xml","purpose":"Voucher"}],"approvalStageLabels":["部门负责人审批","公司领导审批"]},
        "approvalPlan":{"steps":[{"stage":0,"approverName":"李四","approverUserId":2,"actedByName":"李四","actedByUserId":2,"actedAt":"2026-10-28T10:00:00+08:00","note":"同意","delegationKey":""},{"stage":1,"approverName":"王五","approverUserId":3,"actedByName":"赵六","actedByUserId":4,"actedAt":"2026-10-28T11:00:00+08:00","note":"同意按制度报销","delegationKey":"leader-proxy"}]}});
    let cancelled = AtomicBool::new(false);
    snapshot["applicantJobTitle"] = json!("业务专员");
    for long in [false, true] {
        if long {
            snapshot["request"]["reason"] = json!(
                (0..180)
                    .map(|i| format!("完整内容{i:04} 中英文 sample text"))
                    .collect::<Vec<_>>()
                    .join("\n")
            );
        }
        let data = approval::data(&snapshot, "2026-10-28T11:01:00+08:00").unwrap();
        let document = render_design(&data, &design, &cancelled).unwrap();
        if long {
            assert!(document.pages.len() > 1);
        } else {
            assert_eq!(document.pages.len(), 1);
        }
        let svg = document
            .pages
            .iter()
            .map(|page| page.svg.as_str())
            .collect::<String>();
        assert!(svg.contains("代理审批"));
        assert!(svg.contains("公司领导审批"));
        if long {
            let mut cursor = 0;
            for index in 0..180 {
                let needle = format!("完整内容{index:04}");
                cursor += svg[cursor..].find(&needle).unwrap() + needle.len();
            }
        }
        let pdf = pdf_document(&document, &fonts, &cancelled).unwrap();
        assert!(pdf.starts_with(b"%PDF-"));
        if let Some(output) = std::env::var_os("EXPORTDOC_OA_PDF_DIR") {
            let output = PathBuf::from(output);
            std::fs::create_dir_all(&output).unwrap();
            std::fs::write(
                output.join(if long {
                    "approval-long.pdf"
                } else {
                    "approval-one-page.pdf"
                }),
                pdf,
            )
            .unwrap();
        }
    }
}

#[test]
fn countersign_and_later_stages_keep_every_opinion_across_pages() {
    let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../..");
    let fonts = Fonts::new(&root.join("Resources/Fonts/OpenSource/NotoSansCJKsc-Regular.otf"));
    let _scope = fonts.enter().unwrap();
    let design = report_template_format::decode(include_bytes!(
        "../../../Templates/Office/approval_sheet_template.dtpl"
    ))
    .unwrap();
    let steps = (0..6).map(|person|json!({"stage":person/2,"approverName":format!("会签人{person}"),"approverUserId":person+1,"actedAt":"2026-10-28T10:00:00+08:00","note":(0..40).map(|line|format!("意见{person}项{line:03} 逐项核实")).collect::<Vec<_>>().join("\n")})).collect::<Vec<_>>();
    let data = approval::data(&json!({"requestId":42,"submissionNumber":3,"formName":"采购申请审批单","companyName":"示例公司","departmentName":"业务部","submittedAt":"2026-10-28T09:00:00+08:00","request":{"kind":"purchase","title":"会签测试","reason":"完整意见","employeeName":"张三","approvalStageLabels":["部门会签","公司领导审批","补充审批"]},"approvalPlan":{"steps":steps}}),"2026-10-28T11:00:00+08:00").unwrap();
    let document = render_design(&data, &design, &AtomicBool::new(false)).unwrap();
    assert!(document.pages.len() > 1);
    let svg = document
        .pages
        .iter()
        .map(|p| p.svg.as_str())
        .collect::<String>();
    for person in 0..6 {
        for line in 0..40 {
            assert_eq!(svg.matches(&format!("意见{person}项{line:03}")).count(), 1);
        }
    }
    assert!(svg.contains("补充审批"));
    for page in &document.pages {
        assert!(page.svg.contains("申请 #42"));
    }
}
