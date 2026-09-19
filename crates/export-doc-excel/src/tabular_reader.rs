//! Bounded CSV/OOXML reading for business directories. No paths or database writes.
use crate::{Check, MAX_INPUT, Result, archive::Package};
use calamine::{Data, Reader, open_workbook_auto_from_rs};
use std::{io::Cursor, path::Path};

pub fn read_table(
    bytes: &[u8],
    name: &str,
    maximum_rows: usize,
    check: Check<'_>,
) -> Result<Vec<Vec<String>>> {
    Ok(read_table_data(bytes, name, maximum_rows, check)?.rows)
}
pub struct TableData {
    pub sheet_name: String,
    pub first_row: usize,
    pub rows: Vec<Vec<String>>,
}
pub fn read_table_data(
    bytes: &[u8],
    name: &str,
    maximum_rows: usize,
    check: Check<'_>,
) -> Result<TableData> {
    check()?;
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err("导入文件为空或超过 25 MiB。".into());
    }
    match Path::new(name)
        .extension()
        .and_then(|ext| ext.to_str())
        .unwrap_or("")
        .to_ascii_lowercase()
        .as_str()
    {
        "csv" => Ok(TableData {
            sheet_name: "CSV".into(),
            first_row: 1,
            rows: csv(&decode(bytes)?, maximum_rows, check)?,
        }),
        "xlsx" | "xlsm" => {
            Package::open(bytes, check)?;
            let mut workbook = open_workbook_auto_from_rs(Cursor::new(bytes))
                .map_err(|cause| format!("无法读取 Excel：{cause}"))?;
            let sheet = workbook
                .sheet_names()
                .first()
                .cloned()
                .ok_or("Excel 没有可读取的工作表。")?;
            let range = workbook
                .worksheet_range(&sheet)
                .map_err(|cause| cause.to_string())?;
            let (height, width) = range.get_size();
            if height > maximum_rows + 1 || width > 128 {
                return Err(format!("导入超过 {maximum_rows} 行或 128 列上限。"));
            }
            let rows = range
                .rows()
                .map(|row| {
                    check()?;
                    row.iter()
                        .map(|cell| {
                            if matches!(cell, Data::Error(_)) {
                                return Err("导入文件含错误单元格，请先修正工作簿。".into());
                            }
                            let value = cell.to_string();
                            if value.chars().count() > 1_000_000 {
                                return Err("导入单元格内容超过上限。".into());
                            }
                            Ok(value.trim().to_owned())
                        })
                        .collect()
                })
                .collect::<Result<_>>()?;
            Ok(TableData {
                sheet_name: sheet,
                first_row: range.start().map_or(1, |(row, _)| row as usize + 1),
                rows,
            })
        }
        _ => Err("只支持 .csv、.xlsx 或 .xlsm 文件。".into()),
    }
}
pub fn sheet_names(bytes: &[u8], name: &str, check: Check<'_>) -> Result<Vec<String>> {
    check()?;
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err("导入文件为空或超过 25 MiB。".into());
    }
    match Path::new(name)
        .extension()
        .and_then(|ext| ext.to_str())
        .unwrap_or("")
        .to_ascii_lowercase()
        .as_str()
    {
        "csv" => Ok(vec!["CSV".into()]),
        "xlsx" | "xlsm" => {
            Package::open(bytes, check)?;
            let workbook = open_workbook_auto_from_rs(Cursor::new(bytes))
                .map_err(|cause| format!("无法读取 Excel：{cause}"))?;
            Ok(workbook.sheet_names().to_vec())
        }
        _ => Err("只支持 .csv、.xlsx 或 .xlsm 文件。".into()),
    }
}
pub fn read_sheet_data(
    bytes: &[u8],
    _name: &str,
    sheet_name: &str,
    maximum_rows: usize,
    check: Check<'_>,
) -> Result<TableData> {
    check()?;
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err("导入文件为空或超过 25 MiB。".into());
    }
    Package::open(bytes, check)?;
    let mut workbook = open_workbook_auto_from_rs(Cursor::new(bytes))
        .map_err(|cause| format!("无法读取 Excel：{cause}"))?;
    let names = workbook.sheet_names().to_vec();
    if names.is_empty() {
        return Err("Excel 没有可读取的工作表。".into());
    }
    let sheet = if sheet_name.is_empty() {
        names.first().cloned().unwrap_or_default()
    } else {
        names
            .iter()
            .find(|item| item.trim() == sheet_name.trim())
            .cloned()
            .ok_or_else(|| "指定的工作表不存在。".to_string())?
    };
    let range = workbook
        .worksheet_range(&sheet)
        .map_err(|cause| cause.to_string())?;
    let (height, width) = range.get_size();
    if height > maximum_rows + 1 || width > 128 {
        return Err(format!("导入超过 {maximum_rows} 行或 128 列上限。"));
    }
    let rows = range
        .rows()
        .map(|row| {
            check()?;
            row.iter()
                .map(|cell| {
                    if matches!(cell, Data::Error(_)) {
                        return Err("导入文件含错误单元格，请先修正工作簿。".into());
                    }
                    let value = cell.to_string();
                    if value.chars().count() > 1_000_000 {
                        return Err("导入单元格内容超过上限。".into());
                    }
                    Ok(value.trim().to_owned())
                })
                .collect()
        })
        .collect::<Result<_>>()?;
    Ok(TableData {
        sheet_name: sheet,
        first_row: range.start().map_or(1, |(row, _)| row as usize + 1),
        rows,
    })
}
fn decode(bytes: &[u8]) -> Result<String> {
    if bytes.starts_with(&[0xff, 0xfe, 0, 0]) || bytes.starts_with(&[0, 0, 0xfe, 0xff]) {
        let little = bytes[0] == 0xff;
        if (bytes.len() - 4) % 4 != 0 {
            return Err("CSV UTF-32 编码不完整。".into());
        }
        return bytes[4..]
            .chunks_exact(4)
            .map(|chunk| {
                let value = if little {
                    u32::from_le_bytes(chunk.try_into().unwrap())
                } else {
                    u32::from_be_bytes(chunk.try_into().unwrap())
                };
                char::from_u32(value).ok_or_else(|| "CSV 包含无效 Unicode 字符。".into())
            })
            .collect();
    }
    if bytes.starts_with(&[0xff, 0xfe]) || bytes.starts_with(&[0xfe, 0xff]) {
        let little = bytes[0] == 0xff;
        if (bytes.len() - 2) % 2 != 0 {
            return Err("CSV UTF-16 编码不完整。".into());
        }
        let units: Vec<_> = bytes[2..]
            .chunks_exact(2)
            .map(|chunk| {
                if little {
                    u16::from_le_bytes([chunk[0], chunk[1]])
                } else {
                    u16::from_be_bytes([chunk[0], chunk[1]])
                }
            })
            .collect();
        return String::from_utf16(&units).map_err(|_| "CSV 包含无效 Unicode 字符。".into());
    }
    let bytes = bytes.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(bytes);
    std::str::from_utf8(bytes)
        .map(str::to_owned)
        .map_err(|_| "CSV 必须使用 UTF-8 或带 BOM 的 Unicode 编码。".into())
}
fn csv(input: &str, maximum_rows: usize, check: Check<'_>) -> Result<Vec<Vec<String>>> {
    let input = input.replace("\r\n", "\n").replace('\r', "\n");
    let mut rows = vec![];
    let mut fields = vec![];
    let mut value = String::new();
    let mut quoted = false;
    let mut closed = false;
    let mut started = false;
    let mut field_length = 0;
    for line in input.split_terminator('\n') {
        check()?;
        let mut chars = line.chars().peekable();
        while let Some(ch) = chars.next() {
            if quoted {
                if ch == '"' {
                    if chars.peek() == Some(&'"') {
                        chars.next();
                        value.push('"');
                        field_length += 1;
                    } else {
                        quoted = false;
                        closed = true;
                    }
                } else {
                    value.push(ch);
                    field_length += 1;
                }
            } else if ch == ',' {
                if fields.len() >= 127 {
                    return Err("CSV 列数超过 128 列。".into());
                }
                fields.push(value.trim().to_owned());
                value.clear();
                closed = false;
                started = false;
                field_length = 0;
            } else if ch == '"' {
                if started || closed || !value.is_empty() {
                    return Err("CSV 包含未按 RFC 4180 转义的引号。".into());
                }
                quoted = true;
                started = true;
            } else {
                if closed {
                    return Err("CSV 引号后只能是逗号或记录结束符。".into());
                }
                value.push(ch);
                field_length += 1;
                started = true;
            }
            if field_length > 1_000_000 {
                return Err("CSV 字段内容超过上限。".into());
            }
        }
        if quoted {
            value.push('\n');
            field_length += 1;
            continue;
        }
        fields.push(value.trim().to_owned());
        value.clear();
        rows.push(std::mem::take(&mut fields));
        closed = false;
        started = false;
        field_length = 0;
        if rows.len() > maximum_rows + 1 {
            return Err(format!("导入超过 {maximum_rows} 行上限，请拆分文件。"));
        }
    }
    if quoted {
        return Err("CSV 包含未闭合的引号。".into());
    }
    Ok(rows)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn csv_quotes_unicode_and_limits_match_the_directory_import_contract() {
        let rows = read_table(
            "客户名称,备注\r\n\"Cafe\u{301}, Ltd\",\"第一行\n第二行 \"\"报价\"\"\"".as_bytes(),
            "客户.csv",
            2,
            &|| Ok(()),
        )
        .unwrap();
        assert_eq!(rows[1], ["Cafe\u{301}, Ltd", "第一行\n第二行 \"报价\""]);
        for input in ["name\nx\ny", "name\n\"x", "name\nx\"y", "name\n\"x\" y"] {
            assert!(read_table(input.as_bytes(), "x.csv", 1, &|| Ok(())).is_err());
        }
        let bytes: Vec<_> = [0xff, 0xfe]
            .into_iter()
            .chain("名称\n客户".encode_utf16().flat_map(u16::to_le_bytes))
            .collect();
        assert_eq!(
            read_table(&bytes, "客户.csv", 1, &|| Ok(())).unwrap()[1][0],
            "客户"
        );
        let workbook = crate::table(
            &[("name", "客户名称")],
            &[serde_json::json!({"name":"=1+1"})],
            &|| Ok(()),
        )
        .unwrap();
        assert_eq!(
            read_table(&workbook, "客户.xlsx", 1, &|| Ok(())).unwrap()[1][0],
            "=1+1"
        );
    }
}
