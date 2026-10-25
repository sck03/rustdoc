//! Choose the declared workbook container once; never reinterpret a failed parser.
use crate::{Check, MAX_INPUT, Result, archive};
use calamine::{Reader, Sheets, Xls, Xlsb, Xlsx};
use std::io::Cursor;

pub fn open<'a>(bytes: &'a [u8], check: Check<'_>) -> Result<Sheets<Cursor<&'a [u8]>>> {
    check()?;
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err("Excel 文件为空或超过 25 MiB。".into());
    }
    let source = Cursor::new(bytes);
    let workbook = if bytes.starts_with(&[0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1]) {
        Sheets::Xls(Xls::new(source).map_err(|e| format!("无法读取 Excel：{e}"))?)
    } else {
        archive::validate(bytes, check)?;
        let zip = zip::ZipArchive::new(source.clone()).map_err(|e| e.to_string())?;
        let names = zip
            .file_names()
            .collect::<std::result::Result<Vec<_>, _>>()
            .map_err(|e| format!("Excel 文件条目名称无效：{e}"))?;
        if names
            .iter()
            .any(|name| name.eq_ignore_ascii_case("xl/workbook.xml"))
        {
            Sheets::Xlsx(Xlsx::new(source).map_err(|e| format!("无法读取 Excel：{e}"))?)
        } else if names
            .iter()
            .any(|name| name.eq_ignore_ascii_case("xl/workbook.bin"))
        {
            Sheets::Xlsb(Xlsb::new(source).map_err(|e| format!("无法读取 Excel：{e}"))?)
        } else {
            return Err("文件内容不是受支持的 Excel 工作簿。".into());
        }
    };
    check()?;
    Ok(workbook)
}
