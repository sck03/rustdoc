use std::{
    fs,
    path::{Path, PathBuf},
};

#[cfg(test)]
pub(crate) const MAX_EXPORTER_SEAL_IMAGE_BYTES: u64 = export_doc_engine::engine::SEAL_LIMIT as u64;

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
            (
                "图片文件",
                &["png", "jpg", "jpeg", "bmp", "webp", "tif", "tiff"],
            ),
            ("全部文件", &["*"]),
        ],
    ))
}

#[tauri::command]
pub(crate) fn select_exporter_seal_image_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择印章图片",
        &[("图片文件", &["png", "jpg", "jpeg"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) async fn read_exporter_seal_image_file(
    path: String,
) -> Result<tauri::ipc::Response, String> {
    tauri::async_runtime::spawn_blocking(move || {
        read_seal_image_file(path).map(tauri::ipc::Response::new)
    })
    .await
    .map_err(|cause| format!("印章图片读取任务失败：{cause}"))?
}

pub(crate) fn read_seal_image_file(path: String) -> Result<Vec<u8>, String> {
    if path.trim().is_empty() {
        return Err("印章图片路径不能为空。".into());
    }
    export_doc_engine::engine::read_seal_image(Path::new(path.trim()))
        .map_err(|cause| cause.to_string())
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
pub(crate) fn select_report_template_file() -> Result<Option<String>, String> {
    Ok(pick_file(
        "选择报表模板文件",
        &[("V3 报表模板", &["dtpl"]), ("全部文件", &["*"])],
    ))
}

#[tauri::command]
pub(crate) fn select_save_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(save_file(
        "选择单一窗口交换包保存位置",
        "单一窗口交换包",
        &["swpkg"],
        default_file_name,
        default_directory,
    ))
}

#[tauri::command]
pub(crate) fn select_save_invoice_transfer_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(save_file(
        "选择发票单据包保存位置",
        "发票单据包",
        &["edpkg"],
        default_file_name,
        default_directory,
    ))
}

#[tauri::command]
pub(crate) fn select_save_report_template_package_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(save_file(
        "选择报表模板包保存位置",
        "报表模板包",
        &["edtpl", "zip"],
        default_file_name,
        default_directory,
    ))
}

#[tauri::command]
pub(crate) fn select_save_report_template_file_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(save_file(
        "选择报表模板文件保存位置",
        "V3 报表模板",
        &["dtpl"],
        default_file_name,
        default_directory,
    ))
}

#[tauri::command]
pub(crate) fn select_save_pdf_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(save_file(
        "选择 PDF 保存位置",
        "PDF 文件",
        &["pdf"],
        default_file_name,
        default_directory,
    ))
}

#[tauri::command]
pub(crate) fn select_save_zip_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(save_file(
        "选择 ZIP 保存位置",
        "ZIP 文件",
        &["zip"],
        default_file_name,
        default_directory,
    ))
}

#[tauri::command]
pub(crate) fn select_save_excel_path(
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(save_file(
        "选择 Excel 保存位置",
        "Excel 文件",
        &["xlsx"],
        default_file_name,
        default_directory,
    ))
}

#[tauri::command]
pub(crate) fn select_save_file_path(default_file_name: String) -> Result<Option<String>, String> {
    let file_name = Path::new(&default_file_name)
        .file_name()
        .and_then(|value| value.to_str())
        .filter(|value| !value.is_empty())
        .ok_or_else(|| "文件名无效。".to_owned())?;
    let mut dialog = rfd::FileDialog::new()
        .set_title("选择文件保存位置")
        .set_file_name(file_name);
    if let Some(extension) = Path::new(file_name)
        .extension()
        .and_then(|value| value.to_str())
    {
        dialog = dialog.add_filter("原文件类型", &[extension]);
    }
    Ok(dialog.save_file().map(path_to_string))
}

fn save_file(
    title: &str,
    label: &str,
    extensions: &[&str],
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Option<String> {
    let mut dialog = rfd::FileDialog::new()
        .set_title(title)
        .add_filter(label, extensions);
    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }
    apply_default_directory(dialog, default_directory)
        .save_file()
        .map(path_to_string)
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
