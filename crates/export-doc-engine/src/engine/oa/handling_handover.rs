//! Reassignment creates ordinary business events; it never rewrites request content.
use super::*;

pub(super) fn notify(
    tx: &Connection,
    actor: &Actor,
    previous: &Value,
    saved: &Value,
) -> Result<()> {
    let changed: Vec<_> = saved["handlingServices"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|service| {
            previous["handlingServices"]
                .as_array()
                .into_iter()
                .flatten()
                .any(|old| {
                    old["key"] == service["key"]
                        && old["handlerUserIds"] != service["handlerUserIds"]
                })
        })
        .map(|s| text(s, "key"))
        .collect();
    if changed.is_empty() {
        return Ok(());
    }
    for kind in ["oa-general", "supply-requests", "bookings"] {
        for key in &changed {
            let mut offset = 0;
            loop {
                crate::operation::check()?;
                let (count, rows) = tx.query_records(&RecordQuery {
                    kind,
                    company: &actor.company,
                    handling_key: Some(key),
                    statuses: if kind == "bookings" {
                        &["Approved", "InUse"]
                    } else if kind == "supply-requests" {
                        &["Approved", "Issued"]
                    } else {
                        &["Approved"]
                    },
                    fields: &[
                        ("id", "id"),
                        ("status", "status"),
                        ("isReturnable", "isReturnable"),
                        ("handlingKey", "handlingKey"),
                    ],
                    offset,
                    limit: 100,
                    ..Default::default()
                })?;
                for candidate in rows {
                    if !handling::outstanding(&candidate) {
                        continue;
                    }
                    let mut row =
                        store::get(tx, kind, records::positive(&candidate, "id", "申请")?)?;
                    if let Some(mut plan) = approval::plan(&row)? {
                        use export_doc_domain::approval_graph::{
                            ApproverSource, NodeKind, NodeState,
                        };
                        let indices = plan
                            .nodes
                            .iter()
                            .enumerate()
                            .filter_map(|(i, n)| {
                                (n.kind == NodeKind::Handling
                                    && !n.skipped
                                    && n.state != NodeState::Completed
                                    && n.actors
                                        .iter()
                                        .any(|a| a.origin.source == Some(ApproverSource::Handlers)))
                                .then_some(i)
                            })
                            .collect::<Vec<_>>();
                        if !indices.is_empty() {
                            if plan.generation >= 1000 {
                                return Err(conflict("办理交接超过执行代次容量。"));
                            }
                            let old = plan.generation;
                            plan.generation += 1;
                            for task in &mut plan.steps {
                                if task.acted_by_user_id.is_some()
                                    && task.source_generation.is_none()
                                {
                                    task.source_generation = Some(old);
                                }
                            }
                            let employee = store::get(
                                tx,
                                "people",
                                records::positive(&row, "employeeId", "申请人员")?,
                            )?;
                            for index in indices {
                                let node = &plan.nodes[index];
                                let stage = serde_json::from_value::<
                                    export_doc_domain::approval_policy::ApprovalStage,
                                >(
                                    json!({"key":node.key,"name":node.name,"kind":"Handling","phase":node.phase,"source":"Handlers","approverUserIds":[],"dependsOn":node.depends_on,"decision":"All","selfApproval":"Skip","repeatedApprover":if node.independent_review{"Reject"}else{"Allow"},"conditions":[],"dueHours":node.due_hours,"handlingAction":node.handling_action,"allowAdministration":node.allow_administration}),
                                )?;
                                plan.nodes[index].actors = super::actor_binding::resolve(
                                    tx, actor, &row, &employee, &stage,
                                )?;
                                if plan.nodes[index].state == NodeState::Blocked {
                                    plan.nodes[index].state = NodeState::Pending;
                                    plan.nodes[index].error_code.clear();
                                }
                            }
                            plan.refresh_subflows().map_err(invalid)?;
                            plan.activate(chrono::Utc::now()).map_err(invalid)?;
                            plan.validate(row["ownerUserId"].as_i64().unwrap_or(0))
                                .map_err(invalid)?;
                            row["approvalPlan"] = serde_json::to_value(&plan)?;
                            row = store::save_in_scope(
                                tx,
                                kind,
                                records::positive(&row, "id", "申请")?,
                                row,
                                None,
                                actor,
                                "handling-handover",
                                None,
                            )?;
                        }
                    }
                    let note = format!("办理分工交接至：{}", handling::names(tx, actor, &row)?);
                    if kind == "oa-general" {
                        let event = append_event(tx, actor, &row, "reassign", &note)?;
                        super::super::communication::on_event(
                            tx,
                            actor,
                            &row,
                            &event,
                            &json!({"resource":"office.general"}),
                        )?;
                    } else {
                        super::super::office_events::append(
                            tx, actor, kind, &row, "Reassign", 0, &note,
                        )?;
                    }
                }
                offset += 100;
                if offset >= count {
                    break;
                }
            }
        }
    }
    Ok(())
}
