//! Read only the directory attributes of the official client's task configuration.
//! PINs, encryption settings and notification credentials never leave this parser.
use quick_xml::{Reader, events::Event};
use serde_json::{Value, json};

pub const MAX_SETTINGS_BYTES: usize = 1024 * 1024;

pub fn directories(bytes: &[u8]) -> crate::Result<Value> {
    const INVALID: &str = "客户端目录配置格式无效，请手动选择目录。";
    let (root, _) = crate::xml::read(bytes, MAX_SETTINGS_BYTES, false).map_err(|_| INVALID)?;
    if root != "Settings" {
        return Err(INVALID.into());
    }
    let content = std::str::from_utf8(bytes.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(bytes))
        .map_err(|_| INVALID)?;
    let mut reader = Reader::from_str(content);
    reader.config_mut().expand_empty_elements = true;
    let mut stack = Vec::new();
    let mut result = json!({"customsCooDirectories":null,"agentConsignmentDirectories":null});
    loop {
        match reader.read_event().map_err(|_| INVALID)? {
            Event::Start(tag) => {
                if stack == ["Settings", "FormSettings", "FormSetting"]
                    && tag.name().as_ref() == "SubFormSetting"
                {
                    let mut attrs = std::collections::BTreeMap::new();
                    for attr in tag.attributes() {
                        let attr = attr.map_err(|_| INVALID)?;
                        if matches!(
                            attr.key.as_ref(),
                            "FormId" | "OutBox" | "SentBox" | "InBox" | "FailBox"
                        ) {
                            attrs.insert(
                                attr.key.as_ref().to_owned(),
                                quick_xml::escape::unescape(attr.value.as_ref())
                                    .map_err(|_| INVALID)?
                                    .into_owned(),
                            );
                        }
                    }
                    let field = match attrs.get("FormId").map(String::as_str) {
                        Some("ACD") => Some("agentConsignmentDirectories"),
                        Some("COOIMP") => Some("customsCooDirectories"),
                        _ => None,
                    };
                    if let Some(field) = field {
                        if !result[field].is_null() {
                            return Err(INVALID.into());
                        }
                        let mut boxes = json!({});
                        for (xml, key) in [
                            ("OutBox", "outBox"),
                            ("SentBox", "sentBox"),
                            ("InBox", "inBox"),
                            ("FailBox", "failBox"),
                        ] {
                            let value = attrs.get(xml).ok_or(INVALID)?.trim();
                            if value.is_empty() || value.chars().any(char::is_control) {
                                return Err(INVALID.into());
                            }
                            boxes[key] = json!(value);
                        }
                        result[field] = boxes;
                    }
                }
                stack.push(tag.name().as_ref().to_owned());
            }
            Event::End(_) => {
                stack.pop().ok_or(INVALID)?;
            }
            Event::Eof => break,
            _ => {}
        }
    }
    Ok(result)
}
