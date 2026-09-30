//! Bounded validation of Office containers; documents are never executed/rendered.
use super::error::{Result, invalid};
use std::io::{Cursor, Read};

pub(super) fn office_type(extension: &str, bytes: &[u8]) -> Result<&'static str> {
    match extension {
        "docx" => {
            ooxml(
                bytes,
                "word/document.xml",
                "document",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            )?;
            Ok("application/vnd.openxmlformats-officedocument.wordprocessingml.document")
        }
        "xlsx" => {
            ooxml(
                bytes,
                "xl/workbook.xml",
                "workbook",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            )?;
            Ok("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        }
        "doc" | "xls" => compound(extension, bytes),
        _ => Err(invalid(
            "只支持 PDF、PNG、JPEG、Word（DOC/DOCX）和 Excel（XLS/XLSX）。",
        )),
    }
}
fn ooxml(bytes: &[u8], part: &str, root: &str, content_type: &str) -> Result<()> {
    let mut archive = zip::ZipArchive::new(Cursor::new(bytes))
        .map_err(|_| invalid("Office 文档不是有效的 ZIP 容器。"))?;
    if archive.len() > 2048 {
        return Err(invalid("Office 文档内部文件过多。"));
    }
    let mut total = 0u64;
    let mut manifest = false;
    let mut main = false;
    let mut names = std::collections::HashSet::new();
    for index in 0..archive.len() {
        crate::operation::check()?;
        let mut entry = archive
            .by_index(index)
            .map_err(|_| invalid("Office 文档损坏或已加密。"))?;
        let name = entry.name().to_owned();
        if entry.enclosed_name().is_none()
            || !names.insert(name.clone())
            || name.to_ascii_lowercase().ends_with("vbaproject.bin")
        {
            return Err(invalid("Office 容器包含无效路径、重复文件或宏项目。"));
        }
        total = total
            .checked_add(entry.size())
            .filter(|n| *n <= 50 * 1024 * 1024)
            .ok_or_else(|| invalid("Office 文档展开内容不能超过 50 MiB。"))?;
        let mut content = Vec::new();
        (&mut entry)
            .take(50 * 1024 * 1024 + 1)
            .read_to_end(&mut content)
            .map_err(|_| invalid("Office 文档内容或校验码损坏。"))?;
        if content.len() as u64 != entry.size() {
            return Err(invalid("Office 文档内部长度不符。"));
        }
        if name == "[Content_Types].xml" {
            let mut reader = quick_xml::Reader::from_reader(content.as_slice());
            loop {
                match reader
                    .read_event()
                    .map_err(|_| invalid("Office 内容类型清单损坏。"))?
                {
                    quick_xml::events::Event::Empty(e) | quick_xml::events::Event::Start(e)
                        if e.local_name().as_ref() == "Override" =>
                    {
                        let attrs = e
                            .attributes()
                            .collect::<std::result::Result<Vec<_>, _>>()
                            .map_err(|_| invalid("Office 内容类型属性损坏。"))?;
                        manifest |= attrs.iter().any(|a| {
                            a.key.as_ref() == "PartName" && a.value.as_ref() == format!("/{part}")
                        }) && attrs.iter().any(|a| {
                            a.key.as_ref() == "ContentType" && a.value.as_ref() == content_type
                        });
                    }
                    quick_xml::events::Event::Eof => break,
                    _ => {}
                }
            }
        }
        if name == part {
            let mut reader = quick_xml::Reader::from_reader(content.as_slice());
            loop {
                match reader
                    .read_event()
                    .map_err(|_| invalid("Office 主文档 XML 损坏。"))?
                {
                    quick_xml::events::Event::Start(e) | quick_xml::events::Event::Empty(e) => {
                        main = e.local_name().as_ref() == root;
                        break;
                    }
                    quick_xml::events::Event::Eof => break,
                    _ => {}
                }
            }
        }
    }
    if !manifest || !main {
        return Err(invalid("Office 文档内容与扩展名不符，或缺少主文档。"));
    }
    Ok(())
}
fn compound(extension: &str, bytes: &[u8]) -> Result<&'static str> {
    let mut file = cfb::CompoundFile::open(Cursor::new(bytes))
        .map_err(|_| invalid("DOC/XLS 复合文档损坏。"))?;
    let path = if extension == "doc" {
        "WordDocument"
    } else if file.is_stream("Workbook") {
        "Workbook"
    } else {
        "Book"
    };
    let mut stream = file
        .open_stream(path)
        .map_err(|_| invalid("文档内容与 DOC/XLS 扩展名不符。"))?;
    let mut header = [0u8; 8];
    stream
        .read_exact(&mut header)
        .map_err(|_| invalid("DOC/XLS 主文档不完整。"))?;
    let signature = u16::from_le_bytes([header[0], header[1]]);
    if if extension == "doc" {
        signature != 0xa5ec
    } else {
        !matches!(signature, 0x0009 | 0x0209 | 0x0409 | 0x0809)
    } {
        return Err(invalid("DOC/XLS 主文档标记无效。"));
    }
    Ok(if extension == "doc" {
        "application/msword"
    } else {
        "application/vnd.ms-excel"
    })
}
