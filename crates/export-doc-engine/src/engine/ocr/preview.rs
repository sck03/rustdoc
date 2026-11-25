use super::*;

pub(super) fn convert(service: &NativeService, bytes: &[u8]) -> Result<tasks::FileOutput> {
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err(error(413, "OCR 图片必须非空且不能超过 25 MiB。"));
    }
    let resources = Resources::new(&service.paths);
    resources.validate()?;
    let mut command = resources.command(&service.paths.cache_root);
    command.arg("--preview-stdin");
    let output = controlled_process::run(
        &mut command,
        bytes,
        32 * 1024 * 1024,
        Duration::from_secs(20),
    )?;
    if !output.status.success() {
        return Err(if output.status.code() == Some(2) {
            invalid("图片损坏、格式不支持或尺寸超过限制。")
        } else {
            unavailable("图片预览工具未能完成转换。")
        });
    }
    if !output.stdout.starts_with(&[0xff, 0xd8, 0xff]) {
        return Err(unavailable("图片预览工具返回了无效结果。"));
    }
    Ok(tasks::FileOutput {
        file_name: "OCR预览.jpg".into(),
        media_type: "image/jpeg".into(),
        content: output.stdout,
    })
}
