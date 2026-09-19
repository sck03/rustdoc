//! Sequential OOXML output avoids retaining a second DOM for large audit exports.
use crate::{Check, Result, mapping::address};
use serde_json::Value;
use std::fmt::Write;

pub fn sheet(columns: &[(&str, &str)], rows: &[Value], check: Check<'_>) -> Result<Vec<u8>> {
    if columns.is_empty()
        || columns.len() > 128
        || rows.len() > 50_000
        || columns.len().saturating_mul(rows.len()) > 1_000_000
    {
        return Err("导出表格超过容量上限。".into());
    }
    let mut xml = String::from(
        r#"<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetFormatPr defaultColWidth="18" defaultRowHeight="20"/><sheetData>"#,
    );
    let mut row = |number: u32, values: Vec<Value>| -> Result<()> {
        check()?;
        write!(&mut xml, "<row r=\"{number}\">").expect("String writer");
        for (index, value) in values.into_iter().enumerate() {
            let reference = address(number, index as u32 + 1);
            match value {
                Value::Number(value) => {
                    write!(&mut xml, "<c r=\"{reference}\" t=\"n\"><v>{value}</v></c>")
                        .expect("String writer")
                }
                Value::Bool(value) => write!(
                    &mut xml,
                    "<c r=\"{reference}\" t=\"b\"><v>{}</v></c>",
                    i32::from(value)
                )
                .expect("String writer"),
                Value::Null => {}
                value => {
                    let text = match value {
                        Value::String(text) => text,
                        _ => value.to_string(),
                    };
                    if text.encode_utf16().count() > 32_767
                        || text
                            .chars()
                            .any(|ch| ch < '\u{20}' && !matches!(ch, '\n' | '\r' | '\t'))
                    {
                        return Err("导出字段超过 Excel 单元格限制或包含不支持的控制字符。".into());
                    }
                    write!(&mut xml,"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{}</t></is></c>",
                        quick_xml::escape::escape(&text)).expect("String writer");
                }
            }
        }
        xml.push_str("</row>");
        if xml.len() > 120 * 1024 * 1024 {
            return Err("导出表格解压容量超过 120 MiB，请缩小筛选范围。".into());
        }
        Ok(())
    };
    row(
        1,
        columns
            .iter()
            .map(|(_, label)| Value::String((*label).into()))
            .collect(),
    )?;
    for (index, value) in rows.iter().enumerate() {
        row(
            index as u32 + 2,
            columns.iter().map(|(key, _)| value[*key].clone()).collect(),
        )?;
    }
    xml.push_str("</sheetData>");
    write!(
        &mut xml,
        "<autoFilter ref=\"A1:{}\"/></worksheet>",
        address(rows.len() as u32 + 1, columns.len() as u32)
    )
    .expect("String writer");
    Ok(xml.into_bytes())
}
