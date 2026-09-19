//! Bounded XML 1.0 decoding. No DTD, entity resolver, filesystem or network IO.
use crate::Result;
use quick_xml::{
    Reader,
    events::{BytesStart, Event},
};
use std::collections::BTreeMap;

struct Frame {
    name: String,
    text: String,
    children: bool,
    length: usize,
}

fn attributes(element: &BytesStart<'_>) -> Result<()> {
    for attribute in element.attributes() {
        let attribute = attribute.map_err(|_| "XML 属性格式无效或重复。")?;
        quick_xml::escape::unescape(attribute.value.as_ref()).map_err(|_| "XML 属性实体无效。")?;
    }
    Ok(())
}
fn append(stack: &mut [Frame], value: &str, capture: bool) -> Result<()> {
    if let Some(frame) = stack.last_mut() {
        frame.length = frame
            .length
            .checked_add(value.len())
            .ok_or("XML 字段容量超限。")?;
        let limit = if !capture && frame.name == "FileContent" {
            12 * 1024 * 1024
        } else {
            64 * 1024
        };
        if frame.length > limit {
            return Err("XML 单字段超过容量。".into());
        }
        if capture {
            frame.text.push_str(value);
        }
    } else if !value.trim().is_empty() {
        return Err("XML 根节点外存在文字。".into());
    }
    Ok(())
}
pub fn read(
    bytes: &[u8],
    maximum: usize,
    capture: bool,
) -> Result<(String, BTreeMap<String, String>)> {
    if bytes.is_empty() || bytes.len() > maximum {
        return Err("XML 报文为空或超过容量上限。".into());
    }
    let content = std::str::from_utf8(bytes.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(bytes))
        .map_err(|_| "XML 报文必须使用 UTF-8。")?;
    if content
        .chars()
        .any(|c| matches!(c as u32,0..=8|11..=12|14..=31|0xfffe|0xffff))
    {
        return Err("XML 含非法控制字符。".into());
    }
    let mut reader = Reader::from_str(content);
    reader.config_mut().expand_empty_elements = true;
    let mut stack: Vec<Frame> = Vec::new();
    let mut root = String::new();
    let mut fields = BTreeMap::new();
    let mut declaration = false;
    let mut count = 0;
    loop {
        match reader.read_event().map_err(|_| "XML 报文格式无效。")? {
            Event::DocType(_) => return Err("XML 不允许 DTD 或外部实体。".into()),
            Event::Start(element) => {
                attributes(&element)?;
                count += 1;
                if count > 500_000 || stack.len() >= 64 {
                    return Err("XML 节点数量或嵌套深度超限。".into());
                }
                if stack.is_empty() {
                    if !root.is_empty() {
                        return Err("XML 只能包含一个根节点。".into());
                    }
                    root = element.local_name().as_ref().to_owned();
                }
                if let Some(parent) = stack.last_mut() {
                    parent.children = true;
                }
                stack.push(Frame {
                    name: element.local_name().as_ref().into(),
                    text: String::new(),
                    children: false,
                    length: 0,
                });
            }
            Event::Text(value) => append(&mut stack, &value.xml10_content(), capture)?,
            Event::CData(value) => append(&mut stack, &value.xml10_content(), capture)?,
            Event::GeneralRef(value) => {
                let entity = format!("&{};", value.into_inner());
                let decoded = quick_xml::escape::unescape(&entity).map_err(|_| "XML 实体无效。")?;
                if decoded
                    .chars()
                    .any(|c| matches!(c as u32,0..=8|11..=12|14..=31|0xfffe|0xffff))
                {
                    return Err("XML 字符引用无效。".into());
                }
                append(&mut stack, &decoded, capture)?;
            }
            Event::End(_) => {
                let frame = stack.pop().ok_or("XML 结束节点不匹配。")?;
                if capture && !frame.children && fields.insert(frame.name, frame.text).is_some() {
                    return Err("回执包含重复字段，无法唯一判定业务内容。".into());
                }
            }
            Event::Decl(value) => {
                if declaration
                    || !root.is_empty()
                    || value.version().map_err(|_| "XML 版本声明无效。")? != "1.0"
                {
                    return Err("仅支持单个 XML 1.0 声明。".into());
                }
                if let Some(encoding) = value.encoding() {
                    if !encoding
                        .map_err(|_| "XML 编码声明无效。")?
                        .eq_ignore_ascii_case("utf-8")
                    {
                        return Err("XML 报文必须使用 UTF-8。".into());
                    }
                }
                declaration = true;
            }
            Event::Eof => break,
            _ => {}
        }
    }
    if root.is_empty() || !stack.is_empty() {
        return Err("XML 根节点无效或报文未结束。".into());
    }
    Ok((root, fields))
}
pub fn validate(bytes: &[u8]) -> Result<()> {
    read(bytes, crate::package::MAX_PACKAGE, false).map(|_| ())
}
