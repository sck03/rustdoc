use super::{
    NativeService, auth,
    error::{Result, error, invalid, unavailable},
    media,
    store::Actor,
    tasks::{FileOutput, TaskOutput},
};
use crate::{generated_api::*, paths};
use serde_json::Value;
use std::{
    io::{Seek, SeekFrom, Write},
    path::PathBuf,
};
pub const OPERATIONS: &[Operation] = &[
    START_PDF_MERGE_SAVE_TO_PATH_JOB,
    UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB,
];
pub fn upload(service: &NativeService, actor: &Actor, files: Vec<FileOutput>) -> Result<Value> {
    start(
        service,
        actor,
        UPLOAD_AND_START_PDF_MERGE_DOWNLOAD_JOB,
        files,
        None,
    )
}
fn start(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    files: Vec<FileOutput>,
    destination: Option<PathBuf>,
) -> Result<Value> {
    if files.is_empty() || files.len() > 100 {
        return Err(invalid("请选择 1–100 个 PDF 文件。"));
    }
    let mut total = 0usize;
    for file in &files {
        if !paths::valid_file_name(&file.file_name)
            || !PathBuf::from(&file.file_name)
                .extension()
                .is_some_and(|e| e.eq_ignore_ascii_case("pdf"))
            || !file.content.starts_with(b"%PDF-")
        {
            return Err(invalid("待合并文件须为有效 PDF。"));
        }
        total = total
            .checked_add(file.content.len())
            .filter(|n| *n <= crate::pdf::MAX_MERGE_INPUT)
            .ok_or_else(|| invalid("PDF 总大小超过 128 MiB。"))?;
    }
    let store = service.store.clone();
    let task_actor = actor.clone();
    let library = service.paths.pdfium_path();
    // Request admission still owns the uploaded buffers here. Jobs retain only
    // an anonymous, budgeted file, released on rejection, cancellation or exit.
    let mut input = paths::anonymous_file(&service.paths.cache_root).map_err(unavailable)?;
    {
        let mut writer = service.paths.disk_space.writer(&mut input)?;
        crate::pdf::write_merge_input(files.into_iter().map(|file| file.content), &mut writer)?;
        writer.flush()?;
    }
    input.seek(SeekFrom::Start(0))?;
    service
        .jobs
        .start(actor, operation, "PdfMerge", "合并 PDF", move |_| {
            auth::authorize_operation(&auth::refresh_actor(&store, &task_actor)?, operation, &[])?;
            let _rendering = super::reports::resources::acquire_for_job()?;
            let merged = crate::pdf::merge(input, &library)?;
            auth::authorize_operation(&auth::refresh_actor(&store, &task_actor)?, operation, &[])?;
            let mut result = TaskOutput::file("合并文件.pdf".into(), "application/pdf", merged);
            result.destination = destination;
            Ok(result)
        })
}
pub fn local(service: &NativeService, actor: &Actor, body: &Value) -> Result<Value> {
    if service.provider()? != "SQLite" {
        return Err(error(403, "网页或容器不能读取本机 PDF 路径。"));
    }
    let sources: Vec<PathBuf> = serde_json::from_value(body["sourceFiles"].clone())
        .map_err(|_| invalid("请选择 PDF 文件。"))?;
    if sources.is_empty() || sources.len() > 100 {
        return Err(invalid("请选择 1–100 个 PDF 文件。"));
    }
    let destination = PathBuf::from(super::records::text(body, "destinationPath"));
    paths::ensure_safe_absolute(&destination).map_err(invalid)?;
    if !destination
        .file_name()
        .and_then(|s| s.to_str())
        .is_some_and(paths::valid_file_name)
        || !destination
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("pdf"))
    {
        return Err(invalid("请选择有效的 PDF 输出文件。"));
    }
    let output = if destination.exists() {
        Some(std::fs::canonicalize(&destination)?)
    } else {
        None
    };
    let mut files = vec![];
    let mut remaining = crate::pdf::MAX_MERGE_INPUT;
    for source in sources {
        paths::ensure_safe_absolute(&source).map_err(invalid)?;
        if output
            .as_ref()
            .is_some_and(|path| std::fs::canonicalize(&source).ok().as_ref() == Some(path))
        {
            return Err(invalid("合并文件必须另存，不能覆盖源 PDF。"));
        }
        let content = media::read_local(&source, remaining)?;
        remaining -= content.len();
        files.push(FileOutput {
            file_name: source
                .file_name()
                .and_then(|n| n.to_str())
                .ok_or_else(|| invalid("PDF 文件名无效。"))?
                .into(),
            media_type: "application/pdf".into(),
            content,
        });
    }
    start(
        service,
        actor,
        START_PDF_MERGE_SAVE_TO_PATH_JOB,
        files,
        Some(destination),
    )
}
