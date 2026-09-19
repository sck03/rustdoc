use serde_json::Value;
pub struct Review {
    pub content: String,
    pub summary: String,
    pub truncated: bool,
}
pub fn context(invoice: &Value) -> Result<Review, String> {
    let field = |key: &str| invoice[key].as_str().unwrap_or("").trim();
    if ["letterOfCreditContent", "letterOfCreditNo", "specialTerms"]
        .iter()
        .all(|key| field(key).is_empty())
    {
        return Err("请先导入信用证文本，或至少补充信用证号／信用证要求后再进行审查。".into());
    }
    let original = field("letterOfCreditContent");
    let mut used = 0;
    let content: String = original
        .chars()
        .take_while(|c| {
            used += c.len_utf16();
            used <= 12000
        })
        .collect();
    let truncated = content.len() < original.len();
    let mut text = String::from(
        "请基于以下信用证信息与发票数据进行合规审查，列出不符点、风险等级、依据和修改建议。\n\n【审查边界】\n仅使用当前请求中的发票／信用证草稿字段进行审查。\n同一发票号下的实际数据与报关数据可能独立存在，不要推断或合并另一口径。\n不要引用付款／报销单据作为本次信用证审查依据。\n\n【信用证信息】\n",
    );
    if content.is_empty() {
        text.push_str("未导入信用证原文，仅提供摘要字段进行辅助审查，结论可信度较低。\n");
    }
    text.push_str(&content);
    if truncated {
        text.push_str("\n\n[已截断剩余内容，以控制请求体积]");
    }
    text.push_str("\n\n【发票／单据信息】\n");
    for (key, label) in [
        ("invoiceNo", "发票号"),
        ("type", "数据口径"),
        ("contractNo", "合同号"),
        ("letterOfCreditNo", "信用证号"),
        ("issuingBank", "开证行"),
        ("currency", "币制"),
        ("portOfLoading", "起运港"),
        ("portOfDestination", "目的港"),
        ("paymentTerms", "付款方式"),
        ("tradeTerms", "贸易条款"),
        ("transportMode", "运输方式"),
        ("specialTerms", "特别条款"),
    ] {
        text.push_str(&format!("{label}: {}\n", field(key)));
    }
    text.push_str(&format!("金额: {}\n", invoice["totalAmount"]));
    let summary = [field("invoiceNo"), field("type"), field("letterOfCreditNo")].join(" / ");
    Ok(Review {
        content: text,
        summary,
        truncated,
    })
}
