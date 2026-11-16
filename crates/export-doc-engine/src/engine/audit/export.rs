//! Capture one authorized audit snapshot, then encode Excel after releasing its connection.
use super::*;
use crate::engine::backup_io::Download;
use std::io::{BufReader, BufWriter, Seek, SeekFrom, Write};

// Excel's validation errors are strings. Preserve destination I/O failures separately
// so a full disk is 503 while an oversized user selection remains 400.
struct Destination<'a, W> {
    inner: W,
    failure: &'a mut Option<String>,
}
impl<W> Destination<'_, W> {
    fn record<T>(&mut self, result: std::io::Result<T>) -> std::io::Result<T> {
        if let Err(cause) = &result {
            *self.failure = Some(cause.to_string());
        }
        result
    }
}
impl<W: Write> Write for Destination<'_, W> {
    fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
        let result = self.inner.write(bytes);
        self.record(result)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        let result = self.inner.flush();
        self.record(result)
    }
}
impl<W: Seek> Seek for Destination<'_, W> {
    fn seek(&mut self, position: SeekFrom) -> std::io::Result<u64> {
        let result = self.inner.seek(position);
        self.record(result)
    }
}

pub(super) fn prepare(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
    body: &Value,
) -> Result<(Download, usize)> {
    let criteria = Criteria::parse(body)?;
    let limit = body["maxCount"]
        .as_u64()
        .filter(|n| *n > 0)
        .unwrap_or(50_000)
        .min(50_000) as usize;
    let _working = service.jobs.reserve_download(64 * 1024 * 1024, true)?;
    let mut snapshot =
        crate::paths::anonymous_file(&service.paths.cache_root).map_err(unavailable)?;
    let count = {
        let mut writer =
            BufWriter::with_capacity(64 * 1024, service.paths.disk_space.writer(&mut snapshot)?);
        let count = service
            .store
            .read_for(actor, operation, query, |tx, _actor| {
                scan(tx, &criteria, limit, |rows| {
                    for row in rows {
                        serde_json::to_writer(&mut writer, row)?;
                        writer.write_all(b"\n")?;
                    }
                    Ok(())
                })
            })?;
        writer.flush()?;
        count
    };
    snapshot.seek(SeekFrom::Start(0))?;
    let mut output =
        crate::paths::anonymous_file(&service.paths.cache_root).map_err(unavailable)?;
    let mut source_error = None;
    let mut output_error = None;
    let result = export_doc_excel::write_table(
        &[
            ("id", "编号"),
            ("entityName", "实体"),
            ("entityId", "业务编号"),
            ("action", "操作"),
            ("userId", "操作人"),
            ("timestamp", "时间"),
            ("oldValues", "原值"),
            ("newValues", "新值"),
        ],
        |emit| {
            for row in
                serde_json::Deserializer::from_reader(BufReader::new(snapshot)).into_iter::<Value>()
            {
                let row = row.map_err(|cause| {
                    source_error = Some(cause);
                    "审计快照读取失败。".to_owned()
                })?;
                emit(&row)?;
            }
            Ok(())
        },
        Destination {
            inner: BufWriter::with_capacity(
                64 * 1024,
                service.paths.disk_space.writer(&mut output)?,
            ),
            failure: &mut output_error,
        },
        &|| crate::operation::check().map_err(|e| e.to_string()),
    );
    if let Some(cause) = source_error {
        return Err(unavailable(cause.to_string()));
    }
    crate::operation::check()?;
    if let Some(cause) = output_error {
        return Err(unavailable(cause));
    }
    result.map_err(invalid)?;
    let size = output.metadata()?.len();
    output.seek(SeekFrom::Start(0))?;
    Ok((
        Download::File {
            file_name: "AuditLogs.xlsx".into(),
            media_type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet".into(),
            file: output,
            size,
        },
        count,
    ))
}
