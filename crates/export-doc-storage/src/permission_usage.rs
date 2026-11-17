//! Counts published-scheme bindings without loading account or unit-role bodies.
use crate::{
    Adapter, Result,
    sql::{Statement, object},
};
use serde::Deserialize;
use std::collections::{BTreeMap, BTreeSet};

#[derive(Debug, Default, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct PermissionSchemeUsage {
    pub scheme_id: i64,
    pub account_reference_count: i64,
    pub unit_role_reference_count: i64,
    pub published_version: Option<i64>,
}

pub(crate) fn summaries(
    db: &(impl Adapter + ?Sized),
    ids: &[i64],
) -> Result<BTreeMap<i64, PermissionSchemeUsage>> {
    let ids: Vec<_> = ids
        .iter()
        .copied()
        .collect::<BTreeSet<_>>()
        .into_iter()
        .collect();
    let mut result = BTreeMap::new();
    for chunk in ids.chunks(200) {
        crate::operation::check()?;
        let mut s = Statement::new(db);
        let selected = chunk
            .iter()
            .map(|id| format!("({})", s.number(id)))
            .collect::<Vec<_>>()
            .join(",");
        let obj = object(s.pg);
        let aggregate = if s.pg {
            "jsonb_agg"
        } else {
            "json_group_array"
        };
        let cast = if s.pg { "::text" } else { "" };
        let query = format!(
            "WITH selected(scheme_id) AS (VALUES {selected}),
             usage AS (
               SELECT b.scheme_id,
                 SUM(CASE WHEN principal_kind='Account' THEN 1 ELSE 0 END) AS accounts,
                 SUM(CASE WHEN principal_kind='Unit' THEN 1 ELSE 0 END) AS units
               FROM permission_bindings b JOIN selected s ON s.scheme_id=b.scheme_id GROUP BY b.scheme_id),
             latest AS (
               SELECT v.scheme_id,MAX(version) AS version FROM permission_scheme_versions v
               JOIN selected s ON s.scheme_id=v.scheme_id GROUP BY v.scheme_id)
             SELECT {aggregate}({obj}(
               'schemeId',s.scheme_id,'accountReferenceCount',COALESCE(u.accounts,0),
               'unitRoleReferenceCount',COALESCE(u.units,0),'publishedVersion',v.version)){cast}
             FROM selected s LEFT JOIN usage u ON u.scheme_id=s.scheme_id
             LEFT JOIN latest v ON v.scheme_id=s.scheme_id"
        );
        let rows: Vec<PermissionSchemeUsage> = serde_json::from_value(s.query(db, query)?)?;
        result.extend(rows.into_iter().map(|row| (row.scheme_id, row)));
    }
    Ok(result)
}
