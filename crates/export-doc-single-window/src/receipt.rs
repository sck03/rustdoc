use crate::Result;
use export_doc_domain::single_window::Business;
use serde_json::{Value, json};
pub fn parse(business: Business, bytes: &[u8], name: &str) -> Result<(Value, Option<String>)> {
    let (root, fields) = crate::xml::read(bytes, 10 * 1024 * 1024, true)?;
    let get = |key: &str| fields.get(key).map(|s| s.trim()).unwrap_or("");
    let (kind, reference, code, message, status, time) = match (business, root.as_str()) {
        (Business::Coo, "Receipt") => {
            let status = match get("Channel") {
                "1" => "Received",
                "2" => "Failed",
                _ => match get("RepType") {
                    "2" => "PendingReview",
                    "5" => "Approved",
                    "3" | "6" => "Rejected",
                    "1" => "Failed",
                    _ => "Unknown",
                },
            };
            (
                "CustomsCooBusinessReceipt",
                get("CertNo"),
                get("RepCode"),
                if get("RepAddMsg").is_empty() {
                    get("Note")
                } else {
                    get("RepAddMsg")
                },
                status,
                ["RspGenTime", "ReceiveTime", "SendTime"]
                    .into_iter()
                    .map(get)
                    .find(|s| !s.is_empty()),
            )
        }
        (Business::Coo, "FileRet") => (
            if get("FileName").is_empty() && get("FileType").is_empty() {
                "CustomsCooTechnicalReceipt"
            } else {
                "CustomsCooAttachmentReceipt"
            },
            get("CertNo"),
            get("RetType"),
            get("Note"),
            match get("RetType") {
                "1" | "3" => "Accepted",
                "2" | "4" => "Rejected",
                _ => "Unknown",
            },
            ["ReceiveTime", "SendTime"]
                .into_iter()
                .map(get)
                .find(|s| !s.is_empty()),
        ),
        (Business::Acd, "ImportAgrResponse") => (
            "AgentConsignmentImportResponse",
            get("ConsignNo"),
            get("ResponseCode"),
            get("ResponseMessage"),
            if get("ResponseCode") == "0" {
                "Accepted"
            } else {
                "Rejected"
            },
            None,
        ),
        (Business::Acd, "Signature") => (
            "AgentConsignmentAcd002",
            get("CONSIGN_NO"),
            get("PROC_RESULT"),
            get("PROC_DESC"),
            if get("PROC_RESULT").eq_ignore_ascii_case("S") {
                "Accepted"
            } else {
                "Rejected"
            },
            ["OP_TIME", "send_time"]
                .into_iter()
                .map(get)
                .find(|s| !s.is_empty()),
        ),
        _ => return Err("XML 不是当前业务类型支持的官方回执。".into()),
    };
    Ok((
        json!({"businessType":business.name(),"receiptKind":kind,"referenceNo":reference,"receiptCode":code,"receiptMessage":message,"businessStatus":status,"occurredAt":null,"sourceFileName":name}),
        time.map(str::to_owned),
    ))
}
