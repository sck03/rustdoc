use crate::{
    contracts,
    engine::{
        error::{Result, invalid},
        store::{self, Actor, Store},
    },
};
use export_doc_storage::JobQuery;
use serde_json::Value;

pub(super) fn metadata_fields() -> Vec<(&'static str, &'static str)> {
    let mut fields: Vec<_> = contracts::schema("BackgroundJobSnapshot")["properties"]
        .as_object()
        .expect("snapshot properties")
        .keys()
        .filter(|key| key.as_str() != "retryRequestJson")
        .map(|key| (key.as_str(), key.as_str()))
        .collect();
    for key in [
        "id",
        "versionNumber",
        "ownerUserId",
        "companyScope",
        "departmentId",
    ] {
        if !fields.iter().any(|(name, _)| *name == key) {
            fields.push((key, key));
        }
    }
    fields.push(("_retryOperation", "_retry.operation"));
    fields
}
pub(super) fn list(store: &Store, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    let parameter = |key| {
        query
            .iter()
            .find(|(name, _)| *name == key)
            .map(|(_, v)| v.trim())
            .unwrap_or("")
    };
    let supplied = parameter("status");
    let status = if supplied.is_empty() {
        ""
    } else {
        [
            "Pending",
            "Queued",
            "Running",
            "Canceling",
            "Succeeded",
            "Failed",
            "Canceled",
        ]
        .into_iter()
        .find(|known| known.eq_ignore_ascii_case(supplied))
        .ok_or_else(|| invalid("文件任务状态筛选无效。"))?
    };
    let (page, size, offset) = store::page_parameters(query);
    let keyword = store::normalize(parameter("keyword"));
    let fields = metadata_fields();
    let (total, rows) = store.connection()?.query_jobs(&JobQuery {
        owner: (!actor.admin).then_some(actor.id),
        active: None,
        status,
        keyword: &keyword,
        retention: None,
        fields: &fields,
        offset,
        limit: size as i64,
    })?;
    Ok(contracts::page(
        rows.iter()
            .map(|job| super::persistence::actor_snapshot(actor, job))
            .collect(),
        total as usize,
        page,
        size,
    ))
}

pub(super) fn batch<'a>(
    owner: Option<i64>,
    active: bool,
    fields: &'a [(&'a str, &'a str)],
) -> JobQuery<'a> {
    JobQuery {
        owner,
        active: Some(active),
        status: "",
        keyword: "",
        retention: None,
        fields,
        offset: 0,
        limit: 200,
    }
}
