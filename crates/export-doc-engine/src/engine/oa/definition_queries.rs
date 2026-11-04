//! Public version history and structural differences never modify published facts.
use super::*;
pub(super) fn read(
    tx: &Connection,
    actor: &Actor,
    action: &str,
    query: &[(&str, String)],
) -> Result<Value> {
    let argument = |key| crate::engine::request_values::find(query, key).unwrap_or("");
    if action == "settings-routing-sources" {
        let kind = match argument("source") {
            "Position" => "positions",
            "Responsibility" => "responsibilities",
            "ProjectLeader" => "projects",
            _ => return Err(invalid("请选择岗位、业务职责或项目来源。")),
        };
        let unit = if argument("unitId").is_empty() {
            super::super::units::company(tx, &actor.company)?
        } else {
            store::get(
                tx,
                "companies",
                argument("unitId")
                    .parse::<i64>()
                    .ok()
                    .filter(|id| *id > 0)
                    .ok_or_else(|| invalid("来源单位编号无效。"))?,
            )?
        };
        if unit["isActive"] != true || unit["unitKind"] == "Group" {
            return Err(invalid("请选择启用的经营单位。"));
        }
        let (page, size) = paging(query)?;
        return Ok(tx.foundation_catalog(
            kind,
            records::positive(&unit, "id", "单位")?,
            (page - 1) * size,
            size,
        )?);
    }
    let key = argument("definitionKey");
    if !export_doc_domain::approval_graph::key(key, 80) {
        return Err(invalid("流程编号无效。"));
    }
    if action == "settings-versions" {
        let (page, size) = paging(query)?;
        return Ok(
            json!({"items":tx.workflow_versions(&actor.company,key,(page-1)*size,size)?,"totalCount":tx.workflow_version_next(&actor.company,key)?-1}),
        );
    }
    let version = |name| {
        argument(name)
            .parse::<i64>()
            .ok()
            .filter(|v| *v > 0)
            .ok_or_else(|| invalid("流程版本须为正整数。"))
    };
    let from = version("fromVersion")?;
    let to = version("toVersion")?;
    let load = |version| {
        tx.find_identity(
            "oa-definition-version",
            &store::normalize(&format!("{}:{key}:{version}", actor.company)),
        )?
        .ok_or_else(|| error(404, "发布版本不存在。"))
    };
    let before = load(from)?;
    let after = load(to)?;
    let mut changes = vec![];
    difference(
        "",
        &before["definition"],
        &after["definition"],
        &mut changes,
    );
    Ok(json!({"definitionKey":key,"fromVersion":from,"toVersion":to,"changes":changes}))
}
fn difference(path: &str, before: &Value, after: &Value, changes: &mut Vec<Value>) {
    if before == after {
        return;
    }
    match (before, after) {
        (Value::Object(a), Value::Object(b)) => {
            let keys = a
                .keys()
                .chain(b.keys())
                .collect::<std::collections::BTreeSet<_>>();
            for key in keys {
                difference(
                    &format!("{path}/{key}"),
                    a.get(key).unwrap_or(&Value::Null),
                    b.get(key).unwrap_or(&Value::Null),
                    changes,
                );
            }
        }
        _ => changes.push(json!({"path":path,"before":before,"after":after})),
    }
}
