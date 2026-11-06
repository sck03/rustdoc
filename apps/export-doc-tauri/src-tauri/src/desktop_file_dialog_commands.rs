use std::{
    fs,
    path::{Path, PathBuf},
};

#[cfg(test)]
pub(crate) const MAX_EXPORTER_SEAL_IMAGE_BYTES: u64 = export_doc_engine::engine::SEAL_LIMIT as u64;

#[tauri::command]
pub(crate) async fn select_single_window_package_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
        "选择单一窗口交换包",
        &[
            ("单一窗口交换包", &["swpkg", "edpkg"]),
            ("全部文件", &["*"]),
        ],
    )
    .await)
}

#[tauri::command]
pub(crate) async fn select_invoice_transfer_package_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
        "选择发票单据包",
        &[("发票单据包", &["edpkg"]), ("全部文件", &["*"])],
    )
    .await)
}

#[tauri::command]
pub(crate) async fn select_disaster_recovery_package_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
        "选择持卡机灾难恢复包",
        &[("持卡机灾难恢复包", &["edmrecovery"]), ("全部文件", &["*"])],
    )
    .await)
}

#[tauri::command]
pub(crate) async fn select_pdf_files(window: tauri::WebviewWindow) -> Result<Vec<String>, String> {
    let paths = rfd::AsyncFileDialog::new()
        .set_parent(&window)
        .set_title("选择 PDF 文件")
        .add_filter("PDF 文件", &["pdf"])
        .add_filter("全部文件", &["*"])
        .pick_files()
        .await
        .unwrap_or_default()
        .into_iter()
        .map(|file| path_to_string(file.path().to_owned()))
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) async fn select_email_attachment_files(
    window: tauri::WebviewWindow,
) -> Result<Vec<String>, String> {
    let paths = rfd::AsyncFileDialog::new()
        .set_parent(&window)
        .set_title("选择邮件附件")
        .add_filter(
            "常用附件",
            &["pdf", "xlsx", "xls", "docx", "doc", "zip", "txt"],
        )
        .add_filter("全部文件", &["*"])
        .pick_files()
        .await
        .unwrap_or_default()
        .into_iter()
        .map(|file| path_to_string(file.path().to_owned()))
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) async fn select_customs_coo_attachment_files(
    window: tauri::WebviewWindow,
) -> Result<Vec<String>, String> {
    let paths = rfd::AsyncFileDialog::new()
        .set_parent(&window)
        .set_title("选择原产地证附件")
        .add_filter("常用文件", &["pdf", "jpg", "jpeg", "png", "doc", "docx"])
        .add_filter("全部文件", &["*"])
        .pick_files()
        .await
        .unwrap_or_default()
        .into_iter()
        .map(|file| path_to_string(file.path().to_owned()))
        .collect();

    Ok(paths)
}

#[tauri::command]
pub(crate) async fn select_letter_of_credit_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
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
    )
    .await)
}

#[tauri::command]
pub(crate) async fn select_ocr_image_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
        "选择 OCR 图片",
        &[
            (
                "图片文件",
                &["png", "jpg", "jpeg", "bmp", "webp", "tif", "tiff"],
            ),
            ("全部文件", &["*"]),
        ],
    )
    .await)
}

#[tauri::command]
pub(crate) async fn select_exporter_seal_image_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
        "选择印章图片",
        &[("图片文件", &["png", "jpg", "jpeg"]), ("全部文件", &["*"])],
    )
    .await)
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
pub(crate) async fn select_excel_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
        "选择 Excel 文件",
        &[
            ("Excel 文件", &["xlsx", "xlsm", "xltx", "xltm", "xls"]),
            ("全部文件", &["*"]),
        ],
    )
    .await)
}

#[tauri::command]
pub(crate) async fn select_directory(
    window: tauri::WebviewWindow,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    Ok(apply_default_directory(
        rfd::AsyncFileDialog::new()
            .set_parent(&window)
            .set_title("选择目录"),
        default_directory,
    )
    .await?
    .pick_folder()
    .await
    .map(|file| path_to_string(file.path().to_owned())))
}

#[tauri::command]
pub(crate) async fn select_report_template_package_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
        "选择报表模板包",
        &[("报表模板包", &["edtpl", "zip"]), ("全部文件", &["*"])],
    )
    .await)
}

#[tauri::command]
pub(crate) async fn select_report_template_file(
    window: tauri::WebviewWindow,
) -> Result<Option<String>, String> {
    Ok(pick_file(
        &window,
        "选择报表模板文件",
        &[("V3 报表模板", &["dtpl"]), ("全部文件", &["*"])],
    )
    .await)
}

// Command declarations share the host adapter; filters remain explicit per operation.
macro_rules! save_commands {
    ($($name:ident => ($title:literal, $label:literal, $extensions:expr)),+ $(,)?) => {
        $(
            #[tauri::command]
            pub(crate) async fn $name(
                window: tauri::WebviewWindow,
                default_file_name: Option<String>,
                default_directory: Option<String>,
            ) -> Result<Option<String>, String> {
                save_file(&window, $title, $label, $extensions, default_file_name, default_directory).await
            }
        )+
    };
}
save_commands! {
    select_save_package_path => ("选择单一窗口交换包保存位置", "单一窗口交换包", &["swpkg"]),
    select_save_invoice_transfer_package_path => ("选择发票单据包保存位置", "发票单据包", &["edpkg"]),
    select_save_report_template_package_path => ("选择报表模板包保存位置", "报表模板包", &["edtpl", "zip"]),
    select_save_report_template_file_path => ("选择报表模板文件保存位置", "V3 报表模板", &["dtpl"]),
    select_save_pdf_path => ("选择 PDF 保存位置", "PDF 文件", &["pdf"]),
    select_save_zip_path => ("选择 ZIP 保存位置", "ZIP 文件", &["zip"]),
    select_save_excel_path => ("选择 Excel 保存位置", "Excel 文件", &["xlsx"]),
}

#[tauri::command]
pub(crate) async fn select_save_file_path(
    window: tauri::WebviewWindow,
    default_file_name: String,
) -> Result<Option<String>, String> {
    let file_name = Path::new(&default_file_name)
        .file_name()
        .and_then(|value| value.to_str())
        .filter(|value| !value.is_empty())
        .ok_or_else(|| "文件名无效。".to_owned())?;
    let mut dialog = rfd::AsyncFileDialog::new()
        .set_parent(&window)
        .set_title("选择文件保存位置")
        .set_file_name(file_name);
    if let Some(extension) = Path::new(file_name)
        .extension()
        .and_then(|value| value.to_str())
    {
        dialog = dialog.add_filter("原文件类型", &[extension]);
    }
    Ok(dialog
        .save_file()
        .await
        .map(|file| path_to_string(file.path().to_owned())))
}

async fn save_file(
    window: &tauri::WebviewWindow,
    title: &str,
    label: &str,
    extensions: &[&str],
    default_file_name: Option<String>,
    default_directory: Option<String>,
) -> Result<Option<String>, String> {
    let mut dialog = rfd::AsyncFileDialog::new()
        .set_parent(&window)
        .set_title(title)
        .add_filter(label, extensions);
    if let Some(file_name) = default_file_name
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
    {
        dialog = dialog.set_file_name(file_name);
    }
    Ok(apply_default_directory(dialog, default_directory)
        .await?
        .save_file()
        .await
        .map(|file| path_to_string(file.path().to_owned())))
}

async fn pick_file(
    window: &tauri::WebviewWindow,
    title: &str,
    filters: &[(&str, &[&str])],
) -> Option<String> {
    let mut dialog = rfd::AsyncFileDialog::new()
        .set_parent(&window)
        .set_title(title);
    for (name, extensions) in filters {
        dialog = dialog.add_filter(*name, extensions);
    }

    dialog
        .pick_file()
        .await
        .map(|file| path_to_string(file.path().to_owned()))
}

async fn apply_default_directory(
    dialog: rfd::AsyncFileDialog,
    default_directory: Option<String>,
) -> Result<rfd::AsyncFileDialog, String> {
    let path = tauri::async_runtime::spawn_blocking(move || {
        resolve_existing_directory_candidate(default_directory)
    })
    .await
    .map_err(|_| "默认目录检查未完成。".to_owned())?;
    Ok(if let Some(path) = path {
        dialog.set_directory(path)
    } else {
        dialog
    })
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
