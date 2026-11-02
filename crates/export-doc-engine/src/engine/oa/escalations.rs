//! Published escalation candidates are frozen; every transfer rechecks live authority.
use super::*;
use export_doc_domain::approval_graph::{ActorBinding, EscalationAction, NodeKind, NodeState};
pub(super) fn tick(service: &NativeService, now: chrono::DateTime<chrono::Utc>) -> Result<()> {
    let nodes = service
        .store
        .read(|tx| Ok(tx.due_workflow_nodes(&now.to_rfc3339())?))?;
    for entry in nodes {
        crate::operation::check()?;
        service.store.transaction(|tx| {
            let id=records::positive(&entry,"requestId","超时申请")?;
            let kind=text(&entry,"requestKind");
            let mut row=store::get(tx,&kind,id)?;
            let Some(mut plan)=approval::plan(&row)?else{return Err(super::super::error::unavailable("流程实例缺失。"));};
            if row["submissionNumber"]!=entry["revision"] || json!(plan.generation)!=entry["generation"] || !plan.suspended_at.is_empty(){return Ok(());}
            let Some(index)=plan.nodes.iter().position(|n|json!(n.key)==entry["nodeKey"] && json!(n.due_at)==entry["dueAt"] && n.state==NodeState::Active)else{return Ok(());};
            let actor=system_actor(service,&row);
            let mut bindings=primary(&plan,index);
            let (action,note)=if plan.nodes[index].reminder_level==0 {
                plan.nodes[index].reminder_level=1;
                ("remind","节点时限已到，请办理或交接。")
            }else{
                let node=&plan.nodes[index];let cursor=usize::from(node.escalation_cursor);
                let Some(escalation)=node.escalations.get(cursor)else{return Ok(());};
                let at=chrono::DateTime::parse_from_rfc3339(&node.next_escalation_at).map_err(|_|super::super::error::unavailable("升级期限损坏。"))?;
                if at>now{return Ok(());}
                let targets=node.escalation_targets.get(cursor).cloned().unwrap_or_default();
                let command=escalation.action;
                plan.nodes[index].escalation_cursor+=1;
                match command {
                    EscalationAction::Remind=>("remind","节点已超时，按发布政策再次提醒。"),
                    EscalationAction::NotifySupervisor=>{bindings=targets;("supervisor-notify","节点超时已升级，请督办并核对责任身份。")},
                    EscalationAction::Reassign=>{
                        if transfer(tx,&actor,&row,&mut plan,index,&targets)? {bindings=primary(&plan,index);("scheduled-reassign","按发布的升级政策移交未签署任务。")}
                        else {plan.nodes[index].state=NodeState::Blocked;plan.nodes[index].error_code="EscalationTargetUnavailable".into();("workflow-exception","升级接任身份已失效或违反独立性，等待授权管理员处理。")}
                    },
                    EscalationAction::Exception=>{plan.nodes[index].state=NodeState::Blocked;plan.nodes[index].error_code="EscalationExhausted".into();("workflow-exception","节点持续超时，已进入异常队列。")},
                }
            };
            plan.refresh_escalation_deadlines().map_err(invalid)?;
            plan.refresh_subflows().map_err(invalid)?;
            row["approvalPlan"]=serde_json::to_value(&plan)?;
            let saved=store::save_in_scope(tx,&kind,id,row,None,&actor,action,None)?;
            let event=store::save_in_scope(tx,"oa-event",0,json!({"requestId":id,"requestVersion":saved["versionNumber"],"action":action,"actorName":actor.name,"occurredAt":now.to_rfc3339(),"note":note,"executionIdentity":"System","nodeKey":entry["nodeKey"]}),None,&actor,action,Some(&saved))?;
            tx.append_workflow_operation(&json!({"requestId":id,"submissionNumber":entry["revision"],"generation":plan.generation,"actorId":null,"contextId":"","command":action,"reason":note,"occurredAt":now.to_rfc3339(),"executionIdentity":"System","details":{"nodeKey":entry["nodeKey"],"escalationCursor":plan.nodes[index].escalation_cursor}}))?;
            super::super::communication::to_bindings(tx,&actor,&saved,&event,&bindings)?;
            Ok(())
        })?;
    }
    Ok(())
}
fn primary(plan: &export_doc_domain::approval::ApprovalPlan, index: usize) -> Vec<ActorBinding> {
    let mut result = plan.nodes[index].actors.clone();
    result.extend(
        plan.steps
            .iter()
            .filter(|s| s.stage == index && s.status == "Waiting")
            .map(|s| ActorBinding {
                user_id: s.approver_user_id,
                person_id: s.approver_person_id,
                name: s.approver_name.clone(),
                context: s.approver_context.clone(),
                administration: false,
                origin: s.origin.clone(),
            }),
    );
    result
}
fn transfer(
    tx: &Connection,
    actor: &Actor,
    row: &Value,
    plan: &mut export_doc_domain::approval::ApprovalPlan,
    index: usize,
    targets: &[ActorBinding],
) -> Result<bool> {
    let node = &plan.nodes[index];
    let action = super::actor_binding::node_action(node.phase, node.kind, node.handling_action);
    let mut eligible = vec![];
    for target in targets {
        let candidate = match super::super::units::resolve(
            tx,
            target.user_id,
            actor.edition,
            &target.context,
        ) {
            Ok(candidate) => candidate,
            Err(cause) if matches!(cause.status, Some(400 | 401 | 403 | 404 | 409)) => continue,
            Err(cause) => return Err(cause),
        };
        if !super::fulfillment::binding_available(tx, &candidate, row, target)? {
            continue;
        }
        match approval::qualified_person_for(
            tx,
            &candidate,
            row,
            action,
            node.kind == NodeKind::Approval,
        ) {
            Ok(person) if !plan.signer_conflict(index, candidate.id, Some(person)) => {
                eligible.push(target.clone())
            }
            Ok(_) => {}
            Err(cause) if cause.status == Some(403) => {}
            Err(cause) => return Err(cause),
        }
    }
    if node.kind == NodeKind::Handling {
        if eligible.is_empty() {
            return Ok(false);
        }
        if plan.generation >= 1000 {
            return Ok(false);
        }
        let old = plan.generation;
        plan.generation += 1;
        for step in &mut plan.steps {
            if step.acted_by_user_id.is_some() && step.source_generation.is_none() {
                step.source_generation = Some(old);
            }
        }
        plan.nodes[index].actors = eligible;
        plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
            .map_err(invalid)?;
        return Ok(true);
    }
    let waiting = plan
        .steps
        .iter()
        .filter(|s| s.stage == index && s.status == "Waiting")
        .count();
    if waiting == 0 || eligible.len() < waiting {
        return Ok(false);
    }
    let mut choices = eligible.into_iter();
    for step in plan
        .steps
        .iter_mut()
        .filter(|s| s.stage == index && s.status == "Waiting")
    {
        let target = choices
            .next()
            .ok_or_else(|| conflict("升级接任容量不足。"))?;
        step.approver_user_id = target.user_id;
        step.approver_person_id = target.person_id;
        step.approver_name = target.name;
        step.approver_context = target.context;
        step.origin = target.origin;
    }
    plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
        .map_err(invalid)?;
    Ok(true)
}
fn system_actor(service: &NativeService, row: &Value) -> Actor {
    Actor {
        scope_rules: vec![],
        context: String::new(),
        edition: service.store.edition,
        id: 0,
        name: "流程运行任务".into(),
        company: text(row, "companyScope"),
        department: text(row, "departmentId"),
        departments: vec![],
        oa_assigned_only: false,
        admin: false,
        grants: vec![],
    }
}
