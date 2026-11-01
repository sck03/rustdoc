//! Persist only changed assignments; signed facts are compared with the read snapshot.
use super::snapshot::{Snapshot, signed_task};
use super::{Adapter, Error, Result, Statement, Value, conflict, positive, required};

pub(super) fn persist(
    db: &(impl Adapter + ?Sized),
    request: i64,
    revision: i64,
    generation: i64,
    state: &str,
    nodes: &[Value],
    tasks: &[Value],
    active_tasks: &[String],
    previous: &Snapshot,
) -> Result<()> {
    for task in tasks {
        crate::operation::check()?;
        let position = task["stage"]
            .as_u64()
            .ok_or_else(|| Error::unavailable("任务节点无效。"))? as usize;
        let node = nodes
            .get(position)
            .ok_or_else(|| Error::unavailable("任务引用未知节点。"))?;
        let task_key = required(task, "taskKey")?;
        let source_generation = task["sourceGeneration"].as_i64();
        previous.verify_source(task, generation)?;
        let signed = matches!(
            required(task, "status")?,
            "Approved" | "Rejected" | "Returned"
        );
        if let Some(old) = previous.task(generation, task_key) {
            if signed_task(old) && old != task {
                return Err(conflict("已签署任务不可修改、转派或删除。"));
            }
        }
        let active = state == "Pending"
            && task["status"] == "Waiting"
            && active_tasks.iter().any(|key| key == task_key);
        if previous.task(generation, task_key) == Some(task) && previous.active(task_key) == active
        {
            continue;
        }
        let mut s = Statement::new(db);
        let cols = [
            "request_id",
            "submission",
            "generation",
            "task_key",
            "node_key",
            "stage",
            "assignee_id",
            "assignee_person_id",
            "context_id",
            "assignee_name",
            "acted_by_context",
            "acted_by_id",
            "status",
            "active",
            "independent",
            "due_at",
            "started_at",
            "acted_at",
            "elapsed_seconds",
            "note",
            "acted_by_name",
            "delegation_key",
            "decision",
            "signature",
            "source_generation",
        ];
        let elapsed = if signed {
            let parse = |v: &str| {
                chrono::DateTime::parse_from_rfc3339(v)
                    .map_err(|_| Error::unavailable("流程时间必须为 RFC 3339。"))
            };
            let start = parse(required(node, "startedAt")?)?;
            let end = parse(required(task, "actedAt")?)?;
            if end < start {
                return Err(Error::unavailable("签署时间早于激活时间。"));
            }
            (end - start).num_seconds().to_string()
        } else {
            String::new()
        };
        let actual = task["actedByUserId"]
            .as_i64()
            .map(|v| v.to_string())
            .unwrap_or_default();
        let nullable =
            |s: &mut Statement, v: String| format!("CAST(NULLIF({},'') AS BIGINT)", s.text(v));
        let values = vec![
            s.number(request),
            s.number(revision),
            s.number(generation),
            s.text(task_key),
            s.text(required(node, "key")?),
            s.number(position),
            s.number(positive(task, "approverUserId")?),
            s.number(positive(task, "approverPersonId")?),
            s.text(required(task, "approverContext")?),
            s.text(required(task, "approverName")?),
            s.text(required(task, "actedByContext")?),
            nullable(&mut s, actual),
            s.text(required(task, "status")?),
            s.number(i64::from(active)),
            s.number(i64::from(node["independentReview"] == true)),
            s.text(required(node, "dueAt")?),
            s.text(required(node, "startedAt")?),
            s.text(required(task, "actedAt")?),
            nullable(&mut s, elapsed),
            s.text(required(task, "note")?),
            s.text(required(task, "actedByName")?),
            s.text(required(task, "delegationKey")?),
            s.text(required(task, "decision")?),
            if signed && source_generation.is_none() {
                s.json(task)?
            } else {
                "NULL".into()
            },
            nullable(
                &mut s,
                source_generation.map(|g| g.to_string()).unwrap_or_default(),
            ),
        ];
        let updates = cols[4..]
            .iter()
            .map(|c| format!("{c}=excluded.{c}"))
            .collect::<Vec<_>>()
            .join(",");
        s.execute(db,&format!("INSERT INTO workflow_tasks({}) VALUES({}) ON CONFLICT(request_id,submission,generation,task_key) DO UPDATE SET {updates} WHERE workflow_tasks.signature IS NULL AND workflow_tasks.source_generation IS NULL",cols.join(","),values.join(",")))?;
    }
    Ok(())
}
