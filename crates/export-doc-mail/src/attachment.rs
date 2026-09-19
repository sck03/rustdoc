use std::{io::Cursor, path::Path};
pub const MAX_COUNT: usize = 10;
pub const MAX_SINGLE: usize = 10 * 1024 * 1024;
pub const MAX_TOTAL: usize = 18 * 1024 * 1024;
pub struct Attachment {
    pub name: String,
    pub mime: &'static str,
    pub bytes: Vec<u8>,
}
pub fn inspect(name: &str, bytes: &[u8]) -> Result<&'static str, String> {
    if bytes.len() > MAX_SINGLE {
        return Err("邮件单个附件不能超过 10 MiB。".into());
    }
    let ext = Path::new(name)
        .extension()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_lowercase();
    let (mime, valid) = match ext.as_str() {
        "pdf" => ("application/pdf", bytes.starts_with(b"%PDF-")),
        "png" => ("image/png", bytes.starts_with(b"\x89PNG\r\n\x1a\n")),
        "jpg" | "jpeg" => ("image/jpeg", bytes.starts_with(b"\xff\xd8\xff")),
        "doc" | "xls" => (
            if ext == "doc" {
                "application/msword"
            } else {
                "application/vnd.ms-excel"
            },
            bytes.starts_with(b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1"),
        ),
        "zip" | "docx" | "xlsx" => {
            let archive = zip::ZipArchive::new(Cursor::new(bytes))
                .map_err(|_| "邮件附件不是有效的 ZIP/Office 文件。")?;
            let valid = archive.len() > 0
                && archive.len() <= 10000
                && (ext == "zip"
                    || (archive.file_names().any(|n| n == "[Content_Types].xml")
                        && archive
                            .file_names()
                            .any(|n| n.starts_with(if ext == "docx" { "word/" } else { "xl/" }))));
            (
                match ext.as_str() {
                    "docx" => {
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                    }
                    "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    _ => "application/zip",
                },
                valid,
            )
        }
        "txt" | "csv" => {
            let prefix = &bytes[..bytes.len().min(4096)];
            let controls = prefix
                .iter()
                .filter(|b| **b < 0x20 && !b"\t\n\r".contains(b))
                .count();
            (
                if ext == "csv" {
                    "text/csv"
                } else {
                    "text/plain"
                },
                !bytes.contains(&0) && controls * 20 <= prefix.len().max(1),
            )
        }
        _ => return Err("邮件附件类型不受支持。".into()),
    };
    if valid {
        Ok(mime)
    } else {
        Err("邮件附件内容与扩展名不一致。".into())
    }
}
