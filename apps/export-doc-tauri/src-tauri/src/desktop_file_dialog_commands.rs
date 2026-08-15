use std::{
    fs,
    path::{Path, PathBuf},
};

use base64::{engine::general_purpose::STANDARD, Engine as _};

pub(crate) const MAX_EXPORTER_SEAL_IMAGE_BYTES: u64 = 5 * 1024 * 1024;

#[tauri::command]
pub(crate) fn select_single_window_package_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择单一窗口交换包",
        &[
            ("单一窗口交换包", &["swpkg", "edpkg"]),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_invoice_transfer_package_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择发票单据包",
        &[("发票单据包", &["edpkg"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_disaster_recovery_package_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择持卡机灾难恢复包",
        &[("持卡机灾难恢复包", &["edmrecovery"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_receipt_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择单一窗口回执文件",
        &[("回执文件", &["xml", "acd"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_receipt_files() -> Result<Vec<String>, String> {
    let paths = rfd::FileDialog::new()
        .set_title("选择单一窗口回执文件")
        .add_filter("回执文件", &["xml", "acd"])
        .add_filter("全部文件", &["*"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(path_to_string)
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) fn select_pdf_files() -> Result<Vec<String>, String> {
    let paths = rfd::FileDialog::new()
        .set_title("选择 PDF 文件")
        .add_filter("PDF 文件", &["pdf"])
        .add_filter("全部文件", &["*"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(path_to_string)
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) fn select_email_attachment_files() -> Result<Vec<String>, String> {
    let paths = rfd::FileDialog::new()
        .set_title("选择邮件附件")
        .add_filter(
            "常用附件",
            &["pdf", "xlsx", "xls", "docx", "doc", "zip", "txt"],
        )
        .add_filter("全部文件", &["*"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(path_to_string)
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) fn select_customs_coo_attachment_files() -> Result<Vec<String>, String> {
    let paths = rfd::FileDialog::new()
        .set_title("选择原产地证附件")
        .add_filter("常用文件", &["pdf", "jpg", "jpeg", "png", "doc", "docx"])
        .add_filter("全部文件", &["*"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(path_to_string)
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) fn select_letter_of_credit_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择信用证文件",
        &[
            (
                "信用证文件",
                &[
                    "pdf", "txt", "md", "csv", "json", "xml", "png", "jpg", "jpeg", "bmp", "gif",
                    "tif", "tiff", "webp",
                ],
            ),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_ocr_image_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择 OCR 图片",
        &[
            ("图片文件", &["png", "jpg", "jpeg", "bmp", "tif", "tiff"]),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_exporter_seal_image_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择出口商印章图片",
        &[
            ("图片文件", &["png", "jpg", "jpeg", "gif", "webp"]),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn read_exporter_seal_image_file_as_data_url(path: String) -> Result<String, String> {
    let trimmed = path.trim();
    if trimmed.is_empty() {
        return Err("印章图片路径不能为空。".to_owned());
    }

    let input = PathBuf::from(trimmed);
    let metadata = fs::metadata(&input)
        .map_err(|error| format!("无法读取印章图片 '{}': {error}", input.display()))?;
    if !metadata.is_file() {
        return Err("印章图片必须是文件。".to_owned());
    }
    if metadata.len() == 0 || metadata.len() > MAX_EXPORTER_SEAL_IMAGE_BYTES {
        return Err("印章图片不能为空且不能超过 5 MB。".to_owned());
    }

    let bytes = fs::read(&input)
        .map_err(|error| format!("无法读取印章图片 '{}': {error}", input.display()))?;
    let mime_type = exporter_seal_image_mime_type(&input, &bytes)?;
    Ok(format!(
        "data:{mime_type};base64,{}",
        STANDARD.encode(bytes)
    ))
}

#[tauri::command]
pub(crate) fn select_excel_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择 Excel 文件",
        &[
            ("Excel 文件", &["xlsx", "xlsm", "xltx", "xltm", "xls"]),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_directory(
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(apply_default_directory(
        rfd::FileDialog::new().set_title("选择目录"),
        default_directory,
    )
    .pick_folder()
    .map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_report_template_package_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择报表模板包",
        &[("报表模板包", &["edtpl", "zip"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_save_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择单一窗口交换包保存位置")
        .add_filter("单一窗口交换包", &["swpkg"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_invoice_transfer_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择发票单据包保存位置")
        .add_filter("发票单据包", &["edpkg"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_report_template_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择报表模板包保存位置")
        .add_filter("报表模板包", &["edtpl", "zip"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_pdf_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择 PDF 保存位置")
        .add_filter("PDF 文件", &["pdf"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_zip_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择 ZIP 保存位置")
        .add_filter("ZIP 文件", &["zip"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

#[tauri::command]
pub(crate) fn select_save_excel_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择 Excel 保存位置")
        .add_filter("Excel 文件", &["xlsx"]);

    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }

    dialog = apply_default_directory(dialog, default_directory);

    Ok(dialog.save_file().map(path_to_string))
}

fn pick_file(title: &str, filters: &[(&str, &[&str])]) -> Option<String> {
    let mut dialog = rfd::FileDialog::new().set_title(title);
    for (name, extensions) in filters {
        dialog = dialog.add_filter(*name, extensions);
    }

    dialog.pick_file().map(path_to_string)
}

fn apply_default_directory(
    dialog: rfd::FileDialog,
    default_directory: Option<String>,
) -> rfd::FileDialog {
    if let Some(path) = resolve_existing_directory_candidate(default_directory) {
        dialog.set_directory(path)
    } else {
        dialog
    }
}

fn resolve_existing_directory_candidate(default_directory: Option<String>) -> Option<PathBuf> {
    let directory = default_directory
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())?;
    let path = PathBuf::from(directory);
    fs::metadata(&path)
        .ok()
        .filter(|metadata| metadata.is_dir())
        .map(|_| path)
}

#[cfg(test)]
pub(crate) fn is_existing_directory_candidate(default_directory: Option<String>) -> bool {
    resolve_existing_directory_candidate(default_directory).is_some()
}

fn path_to_string(path: PathBuf) -> String {
    path.to_string_lossy().into_owned()
}

fn exporter_seal_image_mime_type(path: &Path, bytes: &[u8]) -> Result<&'static str, String> {
    let detected =
        if bytes.len() >= 8 && bytes[..8] == [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a] {
            ("png", "image/png")
        } else if bytes.len() >= 4
            && bytes[..3] == [0xff, 0xd8, 0xff]
            && bytes[bytes.len() - 2..] == [0xff, 0xd9]
        {
            ("jpeg", "image/jpeg")
        } else if bytes.len() >= 6 && (&bytes[..6] == b"GIF87a" || &bytes[..6] == b"GIF89a") {
            ("gif", "image/gif")
        } else if bytes.len() >= 12 && &bytes[..4] == b"RIFF" && &bytes[8..12] == b"WEBP" {
            ("webp", "image/webp")
        } else {
            return Err("印章图片格式无效；仅支持 PNG、JPEG、GIF 或 WebP。".to_owned());
        };

    let extension = path
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or_default()
        .to_ascii_lowercase();
    let extension_matches = match detected.0 {
        "png" => extension == "png",
        "jpeg" => extension == "jpg" || extension == "jpeg",
        "gif" => extension == "gif",
        "webp" => extension == "webp",
        _ => false,
    };
    if !extension_matches {
        return Err("印章图片扩展名与实际图片格式不一致。".to_owned());
    }

    Ok(detected.1)
}
