use super::{FileOutput, TaskOutput};
use crate::{
    contracts,
    engine::{
        error::{Result, conflict, error, invalid, unavailable},
        media::digest,
        records::text,
        store::{self, Actor, Connection, Store},
    },
    paths::{nonce, valid_file_name},
};
use export_doc_storage::BlobWrite;
use serde_json::{Value, json};

pub(super) const KIND: &str = "background-jobs";
const OUTPUT: &str = "job-output";
const MAX_OUTPUT: usize = 64 * 1024 * 1024;

pub(super) fn active(job: &Value) -> bool {
    matches!(
        text(job, "status").as_str(),
        "Pending" | "Queued" | "Running"
    )
}
fn accessible(actor: &Actor, job: &Value) -> bool {
    actor.admin || job["requestedByUserId"] == actor.id
}
fn lookup(connection: &Connection, id: &str) -> Result<Value> {
    connection
        .find_identity(KIND, id)?
        .ok_or_else(|| error(404, "文件任务不存在。"))
}
pub(super) fn checked(connection: &Connection, actor: &Actor, id: &str) -> Result<Value> {
    let job = lookup(connection, id)?;
    if !accessible(actor, &job) {
        return Err(error(403, "没有读取此任务的权限。"));
    }
    Ok(job)
}
fn snapshot(record: &Value) -> Value {
    let mut value = contracts::initial(contracts::schema("BackgroundJobSnapshot"));
    for (key, field) in value.as_object_mut().expect("generated snapshot schema") {
        if let Some(stored) = record.get(key) {
            *field = stored.clone();
        }
    }
    value["retryRequestJson"] = json!("");
    value["canRetry"] = json!(
        matches!(record["status"].as_str(), Some("Failed" | "Canceled"))
            && retry_operation(record).is_some_and(|operation| super::retry::available(
                operation,
                record["_retryInput"] == true
            ))
    );
    value
}
fn retry_operation(record: &Value) -> Option<&str> {
    record["_retryOperation"]
        .as_str()
        .or_else(|| record["_retry"]["operation"].as_str())
}
pub(super) fn actor_snapshot(actor: &Actor, record: &Value) -> Value {
    let mut value = snapshot(record);
    if value["canRetry"] == true {
        let operation = crate::generated_api::ALL_OPERATIONS
            .iter()
            .find(|operation| retry_operation(record) == Some(operation.id));
        value["canRetry"] = json!(operation.is_some_and(|operation| {
            crate::engine::auth::authorize_operation(actor, *operation, &[]).is_ok()
        }));
    }
    value
}
pub(super) fn owner(job: &Value) -> Result<Actor> {
    Ok(Actor {
        edition: Default::default(),
        id: job["requestedByUserId"]
            .as_i64()
            .ok_or_else(|| unavailable("任务所有者缺失。"))?,
        name: text(job, "requestedBy"),
        company: text(job, "companyScope"),
        department: text(job, "departmentId"),
        admin: false,
        grants: vec![],
        departments: vec![],
        oa_assigned_only: false,
    })
}
fn save(connection: &Connection, actor: &Actor, job: Value, action: &str) -> Result<Value> {
    let id = job["id"].as_i64().unwrap_or(0);
    let identity = text(&job, "jobId");
    store::save(connection, KIND, id, job, Some(identity), actor, action)
}

#[cfg(test)]
pub(super) fn create(store: &Store, actor: &Actor, kind: &str, title: &str) -> Result<Value> {
    create_replayable(
        store,
        actor,
        crate::generated_api::GET_JOB,
        kind,
        title,
        None,
    )
}
pub(super) fn create_replayable(
    store: &Store,
    actor: &Actor,
    operation: crate::generated_api::Operation,
    kind: &str,
    title: &str,
    replay: Option<&super::retry::Replay>,
) -> Result<Value> {
    store.transaction_for(actor, operation, &[], |connection, current| {
        create_in(connection, current, kind, title, replay, Some(operation))
    })
}
pub(super) fn create_completed(
    store: &Store,
    actor: &Actor,
    kind: &str,
    title: &str,
) -> Result<Value> {
    store.transaction(|tx| create_in(tx, actor, kind, title, None, None))
}
fn create_in(
    connection: &Connection,
    actor: &Actor,
    kind: &str,
    title: &str,
    replay: Option<&super::retry::Replay>,
    operation: Option<crate::generated_api::Operation>,
) -> Result<Value> {
    let now = store::timestamp();
    let value = contracts::overlay(
        contracts::initial(contracts::schema("BackgroundJobSnapshot")),
        &json!({
            "jobId":nonce().map_err(unavailable)?,"kind":kind,"title":title,
            "status":"Running","statusText":"正在处理","requestedBy":actor.name,
            "requestedByUserId":actor.id,"createdAt":now,"updatedAt":now,"startedAt":now,
            "progressPercent":0,"canCancel":true
        }),
    );
    let mut value = value;
    value["_operation"] = json!(operation.map(|operation| operation.id));
    let mut saved = save(connection, actor, value, "start")?;
    if let Some(replay) = replay {
        replay.persist(connection, &mut saved)?;
        saved = save(connection, actor, saved, "retry-input")?;
    }
    Ok(snapshot(&saved))
}

pub fn finish(store: &Store, actor: &Actor, id: &str, result: Result<TaskOutput>) -> Result<()> {
    finish_controlled(store, actor, id, result, None)
}
pub(super) fn finish_controlled(
    store: &Store,
    actor: &Actor,
    id: &str,
    result: Result<TaskOutput>,
    canceled: Option<&std::sync::atomic::AtomicBool>,
) -> Result<()> {
    let mut publication = None;
    let mut prepared = false;
    let result = store.transaction(|connection| {
        let mut job = checked(connection, actor, id)?;
        if !active(&job) {
            return Ok(());
        }
        let record_id = job["id"]
            .as_i64()
            .ok_or_else(|| unavailable("任务记录编号缺失。"))?;
        connection.delete_blob(record_id, OUTPUT)?;
        let result = result.and_then(|output| {
            if let Some(file) = &output.file {
                if !valid_file_name(&file.file_name)
                    || file.content.len() > MAX_OUTPUT
                    || file.content.is_empty()
                    || file.media_type.contains(['\r', '\n'])
                    || !file.media_type.contains('/')
                {
                    return Err(invalid("任务输出文件名、类型或容量不符合要求。"));
                }
            }
            if usize::from(output.file.is_some()) + usize::from(output.directory.is_some())
                + usize::from(output.managed_file.is_some()) > 1
                || output.destination.is_some() && output.file.is_none()
            {
                return Err(invalid("任务输出来源重复或保存位置缺少对应文件。"));
            }
            Ok(output)
        });
        if job["cancelRequested"] == true || canceled.is_some_and(|flag| flag.load(std::sync::atomic::Ordering::Acquire)) {
            job["status"] = json!("Canceled");
            job["statusText"] = json!("已取消");
            job["detailText"] = json!("任务已取消，未发布输出。");
            job["cancelRequested"] = json!(true);
        } else {
            // Serialize cancellation against final publication. A cancel after
            // this transaction sees a completed task and cannot relabel it.
            let result = result.and_then(|output| {
                if output.file.is_some() || output.directory.is_some() || output.managed_file.is_some() {
                    let current = crate::engine::auth::current_actor_in(connection, actor.id, store.edition)?;
                    let operation = crate::generated_api::ALL_OPERATIONS.iter().find(|operation| job["_operation"].as_str() == Some(operation.id))
                        .ok_or_else(|| unavailable("任务缺少生成的发布授权元数据。"))?;
                    crate::engine::auth::authorize_operation(&current, *operation, &[])?;
                    if let Some(check) = &output.publication { check.validate(connection, &current)?; }
                    if let Some(path) = &output.managed_file {
                        job["_managedOutput"] = super::managed_output::describe(store, &current, path)?;
                    }
                }
                publication = super::output::publish(store, id, &output)?;
                Ok(output)
            });
            match result {
                Ok(output) => {
                    if output.managed_file.is_some() {
                        job["outputPath"] = job["_managedOutput"]["fileName"].clone();
                        if connection.provider() == "SQLite" {
                            let managed = output.managed_file.as_ref().expect("managed output");
                            job["outputPath"] = json!(managed.path.to_str().ok_or_else(|| invalid("备份位置不是有效 Unicode。"))?);
                        }
                    }
                    if let Some(file) = output.file {
                        let digest = digest(&file.content);
                        connection.insert_blob(&BlobWrite {
                            kind: OUTPUT,
                            record_id,
                            file_name: &file.file_name,
                            media_type: &file.media_type,
                            digest: &digest,
                            content: &file.content,
                            created_at: &store::timestamp(),
                        })?;
                        // No server filesystem path is exposed to a browser.
                        job["outputPath"] = json!(file.file_name);
                        if let Some(path) = &output.destination {
                            job["outputPath"] = json!(path.to_str().ok_or_else(|| invalid("输出位置不是有效 Unicode。"))?);
                            job["_savedOutput"] = json!({"kind":"File","path":path,"size":file.content.len(),"digest":digest});
                        }
                    }
                    if let Some(directory) = output.directory {
                        job["outputPath"] = json!(directory.path.to_str().ok_or_else(|| invalid("输出位置不是有效 Unicode。"))?);
                        job["_savedOutput"] = json!({"kind":"Directory","path":directory.path});
                    }
                    job["status"] = json!("Succeeded");
                    job["statusText"] = json!("已完成");
                    job["detailText"] = json!(output.detail);
                    job["progressPercent"] = json!(100);
                }
                Err(cause) => {
                    job["status"] = json!("Failed");
                    job["statusText"] = json!(if cause.status == Some(504) {
                        "处理超时"
                    } else {
                        "处理失败"
                    });
                    job["errorMessage"] = json!(cause.message);
                }
            }
        }
        job["canCancel"] = json!(false);
        if job["status"] == "Succeeded" && job["_retryInput"] == true {
            super::retry::remove_input(connection, record_id)?;
            job["_retryInput"] = json!(false);
        }
        job["completedAt"] = json!(store::timestamp());
        save(connection, actor, job, "finish")?;
        prepared = true;
        Ok(())
    });
    if let Some(publication) = &mut publication {
        if result.is_ok() {
            publication.commit()?;
        } else if !prepared {
            publication.rollback()?;
        }
        // Commit failure can be indeterminate; startup resolves the durable receipt.
    }
    result
}

pub fn recover(store: &Store) -> Result<()> {
    store.transaction(|connection| {
        loop {
            crate::operation::check()?;
            let (_, jobs) = connection.query_jobs(&super::queries::batch(None, true, &[]))?;
            if jobs.is_empty() {
                break;
            }
            for mut job in jobs {
                let actor = owner(&job)?;
                let id = job["id"]
                    .as_i64()
                    .ok_or_else(|| unavailable("任务记录编号缺失。"))?;
                connection.delete_blob(id, OUTPUT)?;
                job["status"] = json!("Failed");
                job["statusText"] = json!("任务中断");
                job["errorMessage"] =
                    json!("上次进程在任务完成前退出，未自动重复执行。请核对结果后重新操作。");
                job["canCancel"] = json!(false);
                job["outputPath"] = json!("");
                job["completedAt"] = json!(store::timestamp());
                save(connection, &actor, job, "recover-interrupted")?;
            }
        }
        Ok(())
    })
}

pub fn get(store: &Store, actor: &Actor, id: &str) -> Result<Value> {
    Ok(actor_snapshot(
        actor,
        &checked(&*store.connection()?, actor, id)?,
    ))
}
pub fn list(store: &Store, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    super::queries::list(store, actor, query)
}
pub fn download(
    store: &Store,
    actor: &Actor,
    id: &str,
    budget: &std::sync::Arc<super::super::backup_io::Budget>,
) -> Result<super::super::backup_io::Download> {
    let connection = store.connection()?;
    let job = downloadable(&connection, actor, id)?;
    if job["_managedOutput"].is_object() {
        drop(connection);
        return super::managed_output::open(store, actor, &job["_managedOutput"]);
    }
    let record_id = job["id"]
        .as_i64()
        .ok_or_else(|| unavailable("任务记录编号缺失。"))?;
    let info = connection
        .blob_info(record_id, OUTPUT)?
        .ok_or_else(|| unavailable("任务输出缺失。"))?;
    let reservation = budget
        .reserve(usize::try_from(info.size).map_err(|_| unavailable("任务输出大小无效。"))?)?;
    let blob = connection
        .blob(record_id, OUTPUT)?
        .ok_or_else(|| unavailable("任务输出缺失，不能提供不完整结果。"))?;
    if blob.content.len() as u64 != info.size
        || blob.digest != info.digest
        || digest(&blob.content) != blob.digest
    {
        return Err(unavailable("任务输出完整性校验失败。"));
    }
    Ok(super::super::backup_io::Download::Memory {
        output: FileOutput {
            file_name: blob.file_name,
            media_type: blob.media_type,
            content: blob.content,
        },
        reservation,
    })
}
fn downloadable(connection: &Connection, actor: &Actor, id: &str) -> Result<Value> {
    let job = checked(connection, actor, id)?;
    if job["status"] != "Succeeded" || text(&job, "outputPath").is_empty() {
        return Err(conflict("任务没有可下载的结果。"));
    }
    if job["_savedOutput"]["kind"] == "Directory" {
        return Err(conflict("此任务已保存到所选文件夹，请从输出位置使用文件。"));
    }
    if job["_outputExpired"] == true {
        return Err(conflict(
            "下载副本已按容量策略回收；已保存的外部文件继续保留，请从输出位置使用或重新生成。",
        ));
    }
    Ok(job)
}
pub(super) fn authorize_download(store: &Store, actor: &Actor, id: &str) -> Result<()> {
    downloadable(&*store.connection()?, actor, id)?;
    Ok(())
}
pub fn cancel(store: &Store, actor: &Actor, id: &str) -> Result<()> {
    store.transaction(|connection| {
        let mut job = checked(connection, actor, id)?;
        if active(&job) && job["cancelRequested"] != true {
            job["cancelRequested"] = json!(true);
            job["statusText"] = json!("正在取消");
            job["canCancel"] = json!(false);
            save(connection, actor, job, "cancel-requested")?;
        }
        Ok(())
    })
}
pub fn cancel_system(store: &Store, id: &str) -> Result<()> {
    let job = lookup(&*store.connection()?, id)?;
    cancel(store, &owner(&job)?, id)
}
pub(super) fn remove(connection: &Connection, actor: &Actor, job: Value) -> Result<()> {
    if !accessible(actor, &job) {
        return Err(error(403, "没有清理此任务的权限。"));
    }
    if active(&job) {
        return Err(conflict("请先取消仍在执行的任务。"));
    }
    let id = job["id"]
        .as_i64()
        .ok_or_else(|| unavailable("任务记录编号缺失。"))?;
    connection.delete_blob(id, OUTPUT)?;
    super::retry::remove_input(connection, id)?;
    if !connection.delete(KIND, id, store::expected(&job))? {
        return Err(conflict("任务已变化，请重试。"));
    }
    Ok(())
}
pub(super) fn expire_output(connection: &Connection, id: i64) -> Result<()> {
    let mut job = store::get(connection, KIND, id)?;
    if active(&job) {
        return Err(conflict("活动任务的输出不能回收。"));
    }
    let actor = owner(&job)?;
    connection.delete_blob(id, OUTPUT)?;
    if !job["_savedOutput"].is_object() {
        job["outputPath"] = json!("");
    }
    job["_outputExpired"] = json!(true);
    let detail = text(&job, "detailText");
    job["detailText"] = json!(if job["_savedOutput"].is_object() {
        format!("{detail} 下载副本已按容量策略回收，用户保存的文件和位置继续保留。")
    } else {
        format!("{detail} 文件结果已按容量保留策略回收，请重新生成。")
    });
    save(connection, &actor, job, "expire-output")?;
    Ok(())
}
pub(super) fn expire_input(connection: &Connection, id: i64) -> Result<()> {
    let mut job = store::get(connection, KIND, id)?;
    if active(&job) {
        return Err(conflict("活动任务的输入不能回收。"));
    }
    super::retry::remove_input(connection, id)?;
    job["_retryInput"] = json!(false);
    if job["status"] != "Succeeded" {
        job["detailText"] = json!(format!(
            "{} 重试输入已按容量保留策略回收，请重新选择源文件。",
            text(&job, "detailText")
        ));
    }
    let actor = owner(&job)?;
    save(connection, &actor, job, "expire-input")?;
    Ok(())
}
pub fn delete(store: &Store, actor: &Actor, id: &str) -> Result<()> {
    store.transaction(|connection| match lookup(connection, id) {
        Ok(job) => remove(connection, actor, job),
        Err(cause) if cause.status == Some(404) => Ok(()),
        Err(cause) => Err(cause),
    })
}
pub fn clear_finished(store: &Store, actor: &Actor) -> Result<usize> {
    store.transaction(|connection| {
        let fields = super::queries::metadata_fields();
        let mut count = 0;
        loop {
            crate::operation::check()?;
            let (_, jobs) = connection.query_jobs(&super::queries::batch(
                (!actor.admin).then_some(actor.id),
                false,
                &fields,
            ))?;
            if jobs.is_empty() {
                break;
            }
            count += jobs.len();
            for job in jobs {
                remove(connection, actor, job)?;
            }
        }
        Ok(count)
    })
}
