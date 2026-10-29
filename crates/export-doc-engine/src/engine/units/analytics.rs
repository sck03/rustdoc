//! Summary access is independent from workflow maintenance and business data access.
use super::*;

pub(in crate::engine) fn summary(
    store: &store::Store,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        auth::authorize(&actor, "organization.analytics", "manage")?;
        let mut root = actor.company.clone();
        if let Some((_, id)) = query.iter().find(|(key, _)| *key == "unitId") {
            let unit = store::get(
                tx,
                "companies",
                id.parse().map_err(|_| invalid("单位编号无效。"))?,
            )?;
            root = text(&unit, "code");
            if !actor.admin && root != actor.company {
                return Err(error(403, "汇总根单位须为当前授权身份的单位。"));
            }
        }
        company(tx, &root)?;
        let include_descendants = if actor.admin {
            true
        } else {
            let id = actor
                .context
                .strip_prefix("unit:")
                .and_then(|c| c.split_once(':'))
                .and_then(|(id, _)| id.parse::<i64>().ok())
                .ok_or_else(|| error(403, "需要明确的统计授权身份。"))?;
            store::get(tx, KIND, id)?["includeDescendants"] == true
        };
        let tree = tree(tx)?;
        let companies: Vec<_> = store::all(tx, "companies")?
            .iter()
            .filter(|u| u["isActive"] == true && u["unitKind"] != "Group")
            .map(|u| text(u, "code"))
            .filter(|code| {
                code == &root
                    || include_descendants
                        && tree
                            .get(code)
                            .is_some_and(|parents| parents.contains(&root))
            })
            .collect();
        let page = |key: &str, default: i64, max: i64| -> Result<i64> {
            let n = query
                .iter()
                .find(|(k, _)| *k == key)
                .map(|(_, v)| v.parse::<i64>().map_err(|_| invalid("分页参数无效。")))
                .transpose()?
                .unwrap_or(default);
            if !(1..=max).contains(&n) {
                return Err(invalid("分页参数超出范围。"));
            }
            Ok(n)
        };
        let (number, size) = (
            page("pageNumber", 1, 1_000_000)?,
            page("pageSize", 20, 100)?,
        );
        let instant = chrono::Utc::now();
        let now = instant.to_rfc3339();
        let since = (instant - chrono::Duration::days(30)).to_rfc3339();
        let (count, items) =
            tx.query_workflow_summary(&export_doc_storage::WorkflowSummaryQuery {
                companies: &companies,
                as_of: &now,
                completed_since: &since,
                offset: (number - 1) * size,
                limit: size,
            })?;
        Ok(json!({"items":items,"totalCount":count,"pageNumber":number,"pageSize":size,"asOf":now,"completedSince":since}))
    })
}
