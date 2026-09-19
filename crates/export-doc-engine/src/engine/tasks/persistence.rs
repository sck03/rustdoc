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
        .all(KIND)?
        .into_iter()
        .find(|job| job["jobId"] == id)
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
            && record["_retry"].is_object()
    );
    value
}
fn actor_snapshot(actor: &Actor, record: &Value) -> Value {
    let mut value = snapshot(record);
    if value["canRetry"] == true {
        let operation = crate::generated_api::ALL_OPERATIONS
            .iter()
            .find(|operation| record["_retry"]["operation"] == operation.id);
        value["canRetry"] = json!(operation.is_some_and(|operation| {
            crate::engine::auth::authorize_operation(actor, *operation, &[]).is_ok()
        }));
    }
    value
}
pub(super) fn owner(job: &Value) -> Result<Actor> {
    Ok(Actor {
        id: job["requestedByUserId"]
            .as_i64()
            .ok_or_else(|| unavailable("任务所有者缺失。"))?,
        name: text(job, "requestedBy"),
        company: text(job, "companyScope"),
        department: text(job, "departmentId"),
        admin: false,
        grants: vec![],
    })
}
fn save(connection: &Connection, actor: &Actor, job: Value, action: &str) -> Result<Value> {
    let id = job["id"].as_i64().unwrap_or(0);
    let identity = text(&job, "jobId");
    store::save(connection, KIND, id, job, Some(identity), actor, action)
}

pub fn create(store: &Store, actor: &Actor, kind: &str, title: &str) -> Result<Value> {
    create_replayable(store, actor, kind, title, None)
}
pub(super) fn create_replayable(
    store: &Store,
    actor: &Actor,
    kind: &str,
    title: &str,
    replay: Option<&super::retry::Replay>,
) -> Result<Value> {
    store.transaction(|connection| {
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
        let mut saved = save(connection, actor, value, "start")?;
        if let Some(replay) = replay {
            replay.persist(connection, &mut saved)?;
            saved = save(connection, actor, saved, "retry-input")?;
        }
        Ok(snapshot(&saved))
    })
}

pub fn finish(store: &Store, actor: &Actor, id: &str, result: Result<TaskOutput>) -> Result<()> {
    store.transaction(|connection| {
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
            Ok(output)
        });
        if job["cancelRequested"] == true {
            job["status"] = json!("Canceled");
            job["statusText"] = json!("已取消");
            job["detailText"] = json!("任务已取消，未发布输出。");
        } else {
            // Serialize cancellation against final publication. A cancel after
            // this transaction sees a completed task and cannot relabel it.
            let result = result.and_then(|output| {
                super::output::publish(&output)?;
                Ok(output)
            });
            match result {
                Ok(output) => {
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
        job["completedAt"] = json!(store::timestamp());
        save(connection, actor, job, "finish")?;
        Ok(())
    })
}

pub fn recover(store: &Store) -> Result<()> {
    store.transaction(|connection| {
        for mut job in connection.all(KIND)?.into_iter().filter(active) {
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
    let parameter = |key: &str| {
        query
            .iter()
            .find(|(name, _)| *name == key)
            .map(|(_, value)| value.trim())
            .unwrap_or("")
    };
    let status = parameter("status");
    if !status.is_empty()
        && ![
            "Pending",
            "Queued",
            "Running",
            "Canceling",
            "Succeeded",
            "Failed",
            "Canceled",
        ]
        .iter()
        .any(|known| known.eq_ignore_ascii_case(status))
    {
        return Err(invalid("文件任务状态筛选无效。"));
    }
    let keyword = parameter("keyword").to_lowercase();
    let mut jobs: Vec<_> = store
        .all(KIND)?
        .iter()
        .filter(|job| accessible(actor, job))
        .filter(|job| {
            status.is_empty()
                || text(job, "status").eq_ignore_ascii_case(status)
                || (status.eq_ignore_ascii_case("Canceling")
                    && job["cancelRequested"] == true
                    && active(job))
        })
        .filter(|job| {
            keyword.is_empty()
                || [
                    "title",
                    "kind",
                    "statusText",
                    "detailText",
                    "requestedBy",
                    "errorMessage",
                ]
                .iter()
                .any(|key| text(job, key).to_lowercase().contains(&keyword))
        })
        .map(|job| actor_snapshot(actor, job))
        .collect();
    jobs.sort_by(|a, b| b["createdAt"].as_str().cmp(&a["createdAt"].as_str()));
    Ok(store::page_only(jobs, query))
}
pub fn download(store: &Store, actor: &Actor, id: &str) -> Result<FileOutput> {
    let connection = store.connection()?;
    let job = checked(&connection, actor, id)?;
    if job["status"] != "Succeeded" || text(&job, "outputPath").is_empty() {
        return Err(conflict("任务没有可下载的结果。"));
    }
    let record_id = job["id"]
        .as_i64()
        .ok_or_else(|| unavailable("任务记录编号缺失。"))?;
    let blob = connection
        .blob(record_id, OUTPUT)?
        .ok_or_else(|| unavailable("任务输出缺失，不能提供不完整结果。"))?;
    if digest(&blob.content) != blob.digest {
        return Err(unavailable("任务输出完整性校验失败。"));
    }
    Ok(FileOutput {
        file_name: blob.file_name,
        media_type: blob.media_type,
        content: blob.content,
    })
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
pub fn delete(store: &Store, actor: &Actor, id: &str) -> Result<()> {
    store.transaction(|connection| match lookup(connection, id) {
        Ok(job) => remove(connection, actor, job),
        Err(cause) if cause.status == Some(404) => Ok(()),
        Err(cause) => Err(cause),
    })
}
pub fn clear_finished(store: &Store, actor: &Actor) -> Result<usize> {
    store.transaction(|connection| {
        let jobs: Vec<_> = connection
            .all(KIND)?
            .into_iter()
            .filter(|job| !active(job) && accessible(actor, job))
            .collect();
        let count = jobs.len();
        for job in jobs {
            remove(connection, actor, job)?;
        }
        Ok(count)
    })
}
