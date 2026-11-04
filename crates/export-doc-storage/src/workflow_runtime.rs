//! Relational workflow state and immutable submitted material. Application code
//! supplies a validated graph; this module owns its storage and read projection.
use crate::{Adapter, Error, ErrorKind, Result};
use serde_json::{Value, json};
mod snapshot;
mod tasks;

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

/// Archives also reconstruct material and plan fields from frozen relations.
pub(crate) fn projects_body(kind: &str) -> bool {
    request_kind(kind) || kind == "oa-submission"
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

use crate::sql::Statement;
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
    if !(1..=1000).contains(&generation) {
        return Err(Error::unavailable("流程执行代次无效。"));
    }
    let nodes = plan["nodes"]
        .as_array()
        .filter(|n| !n.is_empty() && n.len() <= 32)
        .ok_or_else(|| Error::unavailable("流程节点数量无效。"))?;
    let tasks = plan["steps"]
        .as_array()
        .filter(|t| !t.is_empty() && t.len() <= 200)
        .ok_or_else(|| Error::unavailable("流程任务数量无效。"))?;
    let meta = json!({"definitionKey":plan["definitionKey"],"policyVersion":plan["policyVersion"],"notifyUserIds":plan["notifyUserIds"],"calendars":plan["calendars"],"subflows":plan["subflows"]});
    let instance = format!("{request}:{revision}");
    let previous = snapshot::Snapshot::load(db, &instance, request, revision, plan)?;
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
    let participants = s.json(plan.get("participants").unwrap_or(&json!([])))?;
    if s.execute(db,&format!("INSERT INTO workflow_instances(id,request_id,revision,state,generation,definition,suspended_at,paused_seconds,participants) VALUES({key},{r},{v},{st},{g},{m},{suspended},{paused},{participants}) ON CONFLICT(id) DO UPDATE SET state=excluded.state,generation=excluded.generation,suspended_at=excluded.suspended_at,paused_seconds=excluded.paused_seconds,participants=excluded.participants WHERE workflow_instances.definition=excluded.definition"))? == 0 {
        return Err(conflict("在途流程不能更换已绑定的发布版本。"));
    }
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
        let name = s.text(required(subflow, "key")?);
        let keys = s.json(&subflow["nodeKeys"])?;
        let input = s.json(&subflow["inputSnapshot"])?;
        s.execute(db,&format!("INSERT INTO workflow_subinstances(id,parent_instance_id,definition_id,definition_version,submission_id,subflow_key,state,node_keys,input_snapshot) SELECT {key},{parent},{definition},{version},id,{name},'Pending',{keys},{input} FROM request_revisions WHERE request_id={request} AND revision={revision} ON CONFLICT(id) DO NOTHING"))?;
        if let Some(progress) = plan["subflowProgress"].get(required(subflow, "key")?) {
            let mut s = Statement::new(db);
            let key = s.text(format!("{instance}:sub:{index}"));
            let state = s.text(progress["state"].as_str().unwrap_or("Pending"));
            let outcome = s.text(progress["outcome"].as_str().unwrap_or("Pending"));
            let outputs = s.json(&progress["outputs"])?;
            let error = s.text(progress["errorCode"].as_str().unwrap_or(""));
            let control = s.text(progress["controlOutcome"].as_str().unwrap_or(""));
            let compensated = s.number(i64::from(progress["compensationCompleted"] == true));
            let g = s.number(generation);
            s.execute(db,&format!("UPDATE workflow_subinstances SET state={state},outcome={outcome},generation={g},outputs={outputs},error_code={error},control_outcome={control},compensation_completed={compensated} WHERE id={key}"))?;
        }
    }
    if previous.generation != generation {
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
    }
    for (position, node) in nodes.iter().enumerate() {
        crate::operation::check()?;
        if previous.node_unchanged(generation, position, node) {
            continue;
        }
        let mut spec = node.clone();
        if let Some(v) = spec.as_object_mut() {
            v.remove("startedAt");
            v.remove("dueAt");
            for key in [
                "state",
                "execution",
                "errorCode",
                "escalationCursor",
                "reminderLevel",
                "nextEscalationAt",
            ] {
                v.remove(key);
            }
        }
        let mut s = Statement::new(db);
        let (key, g, n, p, body, start, due, phase, state, execution, error) = (
            s.text(&instance),
            s.number(generation),
            s.text(required(node, "key")?),
            s.number(position),
            s.json(&spec)?,
            s.text(required(node, "startedAt")?),
            s.text(required(node, "dueAt")?),
            s.text(required(node, "phase")?),
            s.text(required(node, "state")?),
            if node["execution"].is_null() {
                "NULL".into()
            } else {
                s.json(&node["execution"])?
            },
            s.text(node["errorCode"].as_str().unwrap_or("")),
        );
        let cursor = s.number(node["escalationCursor"].as_u64().unwrap_or(0));
        let reminder = s.number(node["reminderLevel"].as_u64().unwrap_or(0));
        let next = s.text(node["nextEscalationAt"].as_str().unwrap_or(""));
        if s.execute(db,&format!("INSERT INTO node_instances(instance_id,generation,node_key,position,definition,started_at,due_at,phase,execution_state,execution,error_code,escalation_cursor,reminder_level,next_escalation_at) VALUES({key},{g},{n},{p},{body},{start},{due},{phase},{state},{execution},{error},{cursor},{reminder},{next}) ON CONFLICT(instance_id,generation,node_key) DO UPDATE SET started_at=excluded.started_at,due_at=excluded.due_at,execution_state=excluded.execution_state,execution=excluded.execution,error_code=excluded.error_code,escalation_cursor=excluded.escalation_cursor,reminder_level=excluded.reminder_level,next_escalation_at=excluded.next_escalation_at WHERE node_instances.definition=excluded.definition"))?==0 {
            return Err(conflict("运行节点定义不可覆盖；重审或加减签须建立新的执行代次。"));
        }
        let direct = node["actors"]
            .as_array()
            .into_iter()
            .flatten()
            .map(|a| (a, 0));
        let targets = node["escalationTargets"]
            .as_array()
            .into_iter()
            .flatten()
            .enumerate()
            .flat_map(|(i, level)| {
                level
                    .as_array()
                    .into_iter()
                    .flatten()
                    .map(move |a| (a, i + 1))
            });
        for (actor, level) in direct.chain(targets) {
            let mut s = Statement::new(db);
            let instance = s.text(&instance);
            let g = s.number(generation);
            let node = s.text(required(node, "key")?);
            let user = s.number(positive(actor, "userId")?);
            let context = s.text(required(actor, "context")?);
            let origin = s.json(&actor["origin"])?;
            let person = if actor["personId"].is_null() {
                "NULL".into()
            } else {
                s.number(positive(actor, "personId")?)
            };
            let admin = s.number(i64::from(actor["administration"] == true));
            let level = s.number(level);
            s.execute(db,&format!("INSERT INTO workflow_node_actors(instance_id,generation,node_key,actor_id,person_id,context_id,origin,administration,activation_level) VALUES({instance},{g},{node},{user},{person},{context},{origin},{admin},{level}) ON CONFLICT(instance_id,generation,node_key,actor_id,context_id,activation_level) DO NOTHING"))?;
        }
    }
    tasks::persist(
        db,
        request,
        revision,
        generation,
        state,
        nodes,
        tasks,
        active_tasks,
        &previous,
    )?;
    Ok(())
}
