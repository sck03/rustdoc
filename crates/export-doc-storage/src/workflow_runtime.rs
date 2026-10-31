//! Relational workflow state and immutable submitted material. Application code
//! supplies a validated graph; this module owns its storage and read projection.
use crate::{Adapter, Error, ErrorKind, Result, sql};
use serde_json::{Value, json};

pub(crate) fn request_kind(kind: &str) -> bool {
    matches!(
        kind,
        "oa-leave"
            | "oa-overtime"
            | "oa-expense"
            | "oa-travel"
            | "oa-purchase"
            | "oa-general"
            | "oa-personnel"
            | "bookings"
            | "supply-requests"
    )
}

/// Neither a request nor its archive stores a second writable workflow plan.
pub(crate) fn document(kind: &str, body: &Value) -> Value {
    let mut value = body.clone();
    if request_kind(kind) {
        value.as_object_mut().map(|v| v.remove("approvalPlan"));
        if body["approvalPlan"].is_object() {
            value.as_object_mut().map(|v| v.remove("status"));
        }
    } else if kind == "oa-submission" {
        if let Some(v) = value.as_object_mut() {
            for key in [
                "status",
                "request",
                "template",
                "appointment",
                "companyName",
                "departmentName",
                "applicantJobTitle",
                "formName",
                "templateVersion",
                "submittedAt",
                "approvalPlan",
                "decisions",
            ] {
                v.remove(key);
            }
        }
    }
    value
}

struct Statement {
    pg: bool,
    values: Vec<String>,
}
impl Statement {
    fn new(db: &(impl Adapter + ?Sized)) -> Self {
        Self {
            pg: db.provider() == "PostgreSQL",
            values: vec![],
        }
    }
    fn text(&mut self, value: impl ToString) -> String {
        sql::bind(&mut self.values, value, self.pg)
    }
    fn number(&mut self, value: impl ToString) -> String {
        format!("CAST({} AS BIGINT)", self.text(value))
    }
    fn json(&mut self, value: &Value) -> Result<String> {
        let value = self.text(serde_json::to_string(value)?);
        Ok(if self.pg {
            format!("CAST({value} AS JSONB)")
        } else {
            value
        })
    }
    fn execute(self, db: &(impl Adapter + ?Sized), sql: &str) -> Result<u64> {
        db.execute_statement(sql, &self.values)
    }
    fn query(self, db: &(impl Adapter + ?Sized), sql: &str) -> Result<Value> {
        db.query_json(sql, &self.values)
    }
}

fn conflict(message: &str) -> Error {
    Error::new(ErrorKind::Conflict, message)
}
fn required<'a>(value: &'a Value, key: &str) -> Result<&'a str> {
    value[key]
        .as_str()
        .ok_or_else(|| Error::unavailable(format!("流程字段 {key} 缺失。")))
}
fn positive(value: &Value, key: &str) -> Result<i64> {
    value[key]
        .as_i64()
        .filter(|v| *v > 0)
        .ok_or_else(|| Error::unavailable(format!("流程字段 {key} 无效。")))
}

pub(crate) fn freeze(db: &(impl Adapter + ?Sized), id: i64, snapshot: &Value) -> Result<()> {
    let request = positive(snapshot, "requestId")?;
    let revision = positive(snapshot, "submissionNumber")?;
    let mut owner = Statement::new(db);
    let request_parameter = owner.number(request);
    let archive_parameter = owner.number(id);
    let object = if db.provider() == "PostgreSQL" {
        "jsonb_build_object"
    } else {
        "json_object"
    };
    let source = owner.query(db, &format!("SELECT {object}('requestKind',(SELECT kind FROM records WHERE id={request_parameter}),'archiveKind',(SELECT kind FROM records WHERE id={archive_parameter})){}",if db.provider()=="PostgreSQL"{"::text"}else{""}))?;
    if !request_kind(source["requestKind"].as_str().unwrap_or(""))
        || source["archiveKind"] != "oa-submission"
    {
        return Err(conflict("只有 OA 申请与正式提交档案可以建立流程实例。"));
    }
    let mut frozen = snapshot.clone();
    for source in [&mut frozen["request"]] {
        if let Some(v) = source.as_object_mut() {
            for key in [
                "reviewTaskKeys",
                "canReview",
                "submissionId",
                "submissionNumber",
            ] {
                v.remove(key);
            }
        }
    }
    if let Some(v) = frozen.as_object_mut() {
        for key in [
            "status",
            "approvalPlan",
            "decisions",
            "pdf",
            "completedBy",
            "completedAt",
            "completedNote",
            "voidedAt",
        ] {
            v.remove(key);
        }
    }
    let mut s = Statement::new(db);
    let (id, request, revision, body) = (
        s.number(id),
        s.number(request),
        s.number(revision),
        s.json(&frozen)?,
    );
    let count = s.execute(db, &format!("INSERT INTO request_revisions(id,request_id,revision,body) VALUES({id},{request},{revision},{body}) ON CONFLICT(id) DO NOTHING"))?;
    if count == 0 {
        let mut s = Statement::new(db);
        let id = s.number(snapshot["id"].as_i64().unwrap_or(0));
        let result = s.query(
            db,
            &format!(
                "SELECT {} FROM request_revisions WHERE id={id}",
                if db.provider() == "PostgreSQL" {
                    "body::text"
                } else {
                    "body"
                }
            ),
        )?;
        if result != frozen {
            return Err(conflict("已提交材料不可改写；修改须退回并建立新提交版本。"));
        }
    }
    Ok(())
}

pub(crate) fn persist(
    db: &(impl Adapter + ?Sized),
    request: i64,
    revision: i64,
    progress: &crate::WorkflowProgress<'_>,
    plan: &Value,
    active_tasks: &[String],
) -> Result<()> {
    let state = progress.approval;
    let handling = progress.handling;
    if request <= 0 || revision <= 0 {
        return Err(Error::unavailable("流程申请或提交版本无效。"));
    }
    let generation = plan["generation"].as_i64().unwrap_or(1);
    let meta = json!({"definitionKey":plan["definitionKey"],"policyVersion":plan["policyVersion"],"notifyUserIds":plan["notifyUserIds"],"calendars":plan["calendars"],"subflows":plan["subflows"]});
    let instance = format!("{request}:{revision}");
    let mut s = Statement::new(db);
    let (key, r, v, st, g, m, suspended, paused) = (
        s.text(&instance),
        s.number(request),
        s.number(revision),
        s.text(state),
        s.number(generation),
        s.json(&meta)?,
        s.text(plan["suspendedAt"].as_str().unwrap_or("")),
        s.number(plan["pausedSeconds"].as_u64().unwrap_or(0)),
    );
    s.execute(db,&format!("INSERT INTO workflow_instances(id,request_id,revision,state,generation,definition,suspended_at,paused_seconds) VALUES({key},{r},{v},{st},{g},{m},{suspended},{paused}) ON CONFLICT(id) DO UPDATE SET state=excluded.state,generation=excluded.generation,suspended_at=excluded.suspended_at,paused_seconds=excluded.paused_seconds WHERE workflow_instances.definition=excluded.definition"))?;
    if let Some((handling_state, result)) = handling {
        let mut s = Statement::new(db);
        let (r, v, st, result) = (
            s.number(request),
            s.number(revision),
            s.text(handling_state),
            s.text(result),
        );
        s.execute(db,&format!("INSERT INTO handling_tasks(request_id,revision,state,result_status) VALUES({r},{v},{st},{result}) ON CONFLICT(request_id,revision) DO UPDATE SET state=excluded.state,result_status=excluded.result_status"))?;
    }
    let mut s = Statement::new(db);
    let key = s.text(&instance);
    let old = s.query(
        db,
        &format!(
            "SELECT {} FROM workflow_instances WHERE id={key}",
            if db.provider() == "PostgreSQL" {
                "definition::text"
            } else {
                "definition"
            }
        ),
    )?;
    if old != meta {
        return Err(conflict("在途流程不能更换已绑定的发布版本。"));
    }
    for (index, subflow) in plan["subflows"]
        .as_array()
        .into_iter()
        .flatten()
        .enumerate()
    {
        let mut s = Statement::new(db);
        let key = s.text(format!("{instance}:sub:{index}"));
        let parent = s.text(&instance);
        let definition = s.number(positive(subflow, "definitionId")?);
        let version = s.number(positive(subflow, "version")?);
        let request = s.number(request);
        let revision = s.number(revision);
        let name = s.text(format!("{}:{index}", required(subflow, "key")?));
        let keys = s.json(&subflow["nodeKeys"])?;
        s.execute(db,&format!("INSERT INTO workflow_subinstances(id,parent_instance_id,definition_id,definition_version,submission_id,subflow_key,state,node_keys,input_snapshot) SELECT {key},{parent},{definition},{version},id,{name},'Pending',{keys},body FROM request_revisions WHERE request_id={request} AND revision={revision} ON CONFLICT(id) DO NOTHING"))?;
    }
    let nodes = plan["nodes"]
        .as_array()
        .filter(|n| !n.is_empty() && n.len() <= 32)
        .ok_or_else(|| Error::unavailable("流程节点数量无效。"))?;
    let tasks = plan["steps"]
        .as_array()
        .filter(|t| !t.is_empty() && t.len() <= 200)
        .ok_or_else(|| Error::unavailable("流程任务数量无效。"))?;
    let mut s = Statement::new(db);
    let r = s.number(request);
    s.execute(
        db,
        &format!("UPDATE workflow_tasks SET active=0 WHERE request_id={r} AND active=1"),
    )?;
    let mut s = Statement::new(db);
    let r = s.number(request);
    let v = s.number(revision);
    let g = s.number(generation);
    s.execute(db, &format!("UPDATE workflow_tasks SET status='Cancelled',active=0 WHERE request_id={r} AND submission={v} AND generation<>{g} AND status='Waiting'"))?;
    for (position, node) in nodes.iter().enumerate() {
        crate::operation::check()?;
        let mut spec = node.clone();
        if let Some(v) = spec.as_object_mut() {
            v.remove("startedAt");
            v.remove("dueAt");
        }
        let mut s = Statement::new(db);
        let (key, g, n, p, body, start, due) = (
            s.text(&instance),
            s.number(generation),
            s.text(required(node, "key")?),
            s.number(position),
            s.json(&spec)?,
            s.text(required(node, "startedAt")?),
            s.text(required(node, "dueAt")?),
        );
        if s.execute(db,&format!("INSERT INTO node_instances(instance_id,generation,node_key,position,definition,started_at,due_at) VALUES({key},{g},{n},{p},{body},{start},{due}) ON CONFLICT(instance_id,generation,node_key) DO UPDATE SET started_at=excluded.started_at,due_at=excluded.due_at WHERE node_instances.definition=excluded.definition"))?==0 {
            return Err(conflict("运行节点定义不可覆盖；重审或加减签须建立新的执行代次。"));
        }
    }
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
        if let Some(source) = source_generation {
            if source <= 0 || source >= generation {
                return Err(conflict("复用签署的执行代次无效。"));
            }
            let mut s = Statement::new(db);
            let (r, v, g, k) = (
                s.number(request),
                s.number(revision),
                s.number(source),
                s.text(task_key),
            );
            let original=s.query(db,&format!("SELECT {column} FROM workflow_tasks WHERE request_id={r} AND submission={v} AND generation={g} AND task_key={k} AND signature IS NOT NULL",column=if db.provider()=="PostgreSQL" {"signature::text"}else{"signature"}))?;
            let mut expected = task.clone();
            let mut original = original;
            expected
                .as_object_mut()
                .map(|v| v.remove("sourceGeneration"));
            original
                .as_object_mut()
                .map(|v| v.remove("sourceGeneration"));
            if original != expected {
                return Err(conflict("复用材料与原签署不一致，不能复制为新批准。"));
            }
        }
        let signed = matches!(
            required(task, "status")?,
            "Approved" | "Rejected" | "Returned"
        );
        let mut s = Statement::new(db);
        let (r, v, g, k) = (
            s.number(request),
            s.number(revision),
            s.number(generation),
            s.text(task_key),
        );
        let aggregate = if db.provider() == "PostgreSQL" {
            "COALESCE(jsonb_agg(signature),'[]'::jsonb)::text"
        } else {
            "json_group_array(json(signature))"
        };
        let existing=s.query(db,&format!("SELECT {aggregate} FROM workflow_tasks WHERE request_id={r} AND submission={v} AND generation={g} AND task_key={k}"))?;
        if let Some(previous) = existing
            .as_array()
            .and_then(|a| a.first())
            .filter(|p| !p.is_null())
        {
            if previous != task {
                return Err(conflict("已签署任务不可修改、转派或删除。"));
            }
        }
        let active = state == "Pending"
            && task["status"] == "Waiting"
            && active_tasks.iter().any(|key| key == task_key);
        let mut s = Statement::new(db);
        let cols = [
            "request_id",
            "submission",
            "generation",
            "task_key",
            "node_key",
            "stage",
            "assignee_id",
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
