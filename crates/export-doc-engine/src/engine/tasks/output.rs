//! Files in a selected batch folder are published together by one rename.
use super::TaskOutput;
use crate::{
    engine::error::{Result, invalid, unavailable},
    paths,
};
use std::collections::BTreeSet;
pub(super) fn publish(output: &TaskOutput) -> Result<()> {
    if let (Some(destination), Some(file)) = (&output.destination, &output.file) {
        paths::atomic_write(destination, &file.content).map_err(unavailable)?;
    }
    let Some(group) = &output.directory else {
        return Ok(());
    };
    if output.destination.is_some() {
        return Err(invalid("文件任务不能同时写入单文件与批次目录。"));
    }
    let target = &group.path;
    paths::ensure_safe_absolute(target).map_err(invalid)?;
    if target.exists() {
        return Err(invalid("本次导出批次目录已存在，请重新导出。"));
    }
    let parent = target
        .parent()
        .filter(|p| p.is_dir())
        .ok_or_else(|| invalid("请选择存在的输出目录。"))?;
    let mut names = BTreeSet::new();
    let mut total = 0usize;
    if group.files.is_empty() || group.files.len() > 21 {
        return Err(invalid("批次文件数量无效。"));
    }
    for file in &group.files {
        if !paths::valid_file_name(&file.file_name)
            || !names.insert(file.file_name.to_lowercase())
            || file.content.is_empty()
        {
            return Err(invalid("批次文件名重复或内容无效。"));
        }
        total = total
            .checked_add(file.content.len())
            .filter(|n| *n <= 64 * 1024 * 1024)
            .ok_or_else(|| invalid("批次输出超过 64 MiB。"))?;
    }
    let stage = parent.join(format!(
        ".exportdoc-batch-{}",
        paths::nonce().map_err(unavailable)?
    ));
    paths::ensure_safe_absolute(&stage).map_err(invalid)?;
    std::fs::create_dir(&stage)?;
    let result = (|| {
        for file in &group.files {
            crate::operation::check()?;
            paths::atomic_write(&stage.join(&file.file_name), &file.content)
                .map_err(unavailable)?;
        }
        crate::operation::check()?;
        paths::ensure_safe_absolute(target).map_err(invalid)?;
        if target.exists() {
            return Err(invalid("导出目标已存在，当前内容没有被覆盖。"));
        }
        std::fs::rename(&stage, target)?;
        Ok(())
    })();
    if result.is_err() {
        for file in &group.files {
            let path = stage.join(&file.file_name);
            if paths::ensure_safe_absolute(&path).is_ok() {
                let _ = std::fs::remove_file(path);
            }
        }
        if paths::ensure_safe_absolute(&stage).is_ok() {
            let _ = std::fs::remove_dir(stage);
        }
    }
    result
}
