//! Adapt organization use cases without growing the application composition root.
use super::*;
use crate::generated_api::*;

pub(in crate::engine) fn dispatch(
    store: &store::Store,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: &Value,
) -> Option<Result<Value>> {
    use crate::engine::{oa, organization, personnel_primary};
    Some(match operation {
        GET_UNIT_ACCESS_DIRECTORY => directory(store, actor, query),
        GET_UNIT_WORKFLOW_SUMMARY => workflow_summary(store, actor, query),
        GET_UNIT_PERMISSION_TEMPLATES => scheme_directory(store, actor, query),
        SAVE_UNIT_PERMISSION_TEMPLATE => save_scheme(store, actor, body.clone()),
        GET_UNIT_APPROVER_OPTIONS => approvers(store, actor),
        GET_UNIT_MEMBERS => members(store, actor, query),
        SAVE_UNIT_MEMBER => save_member(store, actor, body.clone()),
        GET_UNIT_WORKFLOW_TASKS => oa::managed_tasks(store, actor, query),
        REASSIGN_UNIT_WORKFLOW_TASK => oa::managed_reassign(store, actor, body.clone()),
        SIMULATE_OA_DEFINITION => oa::simulate(store, actor, body.clone()),
        GET_OA_ROUTING_UNITS => oa::routing_units(store, actor),
        SAVE_UNIT_ACCESS => save(store, actor, body.clone()),
        GET_ORGANIZATION_DIRECTORY => organization::directory(store, actor),
        PREVIEW_ORGANIZATION_COMPANY_CHANGE => {
            preview_company_change(store, actor, parameters, body)
        }
        PROMOTE_PERSONNEL_APPOINTMENT => personnel_primary::promote(store, actor, parameters, body),
        LIST_ORGANIZATION_MANAGERS => managers(store, actor, query),
        _ => return None,
    })
}

fn managers(store: &store::Store, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    let company = query
        .iter()
        .find(|(key, _)| *key == "companyCode")
        .map(|(_, value)| value.as_str())
        .unwrap_or(&actor.company);
    auth::authorize(actor, "organization.directory", "view")?;
    if !actor.admin && company != actor.company {
        return Err(error(403, "人员候选超出当前管理单位。"));
    }
    crate::engine::organization::managers(store, query)
}
