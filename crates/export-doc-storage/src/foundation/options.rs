//! Bounded public labels for organization editors; no membership or private data.
use super::*;

pub(crate) fn options(
    db: &(impl Adapter + ?Sized),
    unit: i64,
    kind: &str,
    keyword: &str,
    selected: &[i64],
    offset: i64,
    limit: i64,
) -> Result<Value> {
    let (source, name, active) = match kind {
        "projects" => ("project_scopes r", "r.name", "r.is_active"),
        "departments" => (
            "organization_departments r JOIN organization_labels l ON l.id=r.id",
            "l.name",
            "l.is_active",
        ),
        _ => return Err(Error::unavailable("未知组织选项类别。")),
    };
    if selected.len() > 200 {
        return Err(Error::unavailable("已选组织选项超过上限。"));
    }
    let mut s = Statement::new(db);
    let unit = s.number(unit);
    let term = s.text(keyword);
    let filter = format!(
        "r.unit_id={unit} AND {active}=1 AND ({term}='' OR {} OR {})",
        sql::contains(name, &term, s.pg),
        sql::contains("r.code", &term, s.pg)
    );
    let selected = if selected.is_empty() {
        "0".into()
    } else {
        selected
            .iter()
            .map(|id| s.number(*id))
            .collect::<Vec<_>>()
            .join(",")
    };
    let obj = object(s.pg);
    let body = format!(
        "{obj}('id',CAST(r.id AS TEXT),'code',r.code,'name',{name},'isActive',{})",
        boolean(active, s.pg)
    );
    let limit = limit.clamp(1, 100);
    let items = json_value(
        &aggregate(
            &format!(
                "SELECT {body} AS body FROM {source} WHERE {filter} ORDER BY r.code,r.id LIMIT {limit} OFFSET {}",
                offset.max(0)
            ),
            s.pg,
        ),
        s.pg,
    );
    let selected = json_value(
        &aggregate(
            &format!(
                "SELECT {body} AS body FROM {source} WHERE r.unit_id={unit} AND r.id IN ({selected}) ORDER BY r.code,r.id"
            ),
            s.pg,
        ),
        s.pg,
    );
    let query = read_json(
        format!(
            "SELECT {obj}('items',{items},'selected',{selected},'totalCount',(SELECT COUNT(*) FROM {source} WHERE {filter}))"
        ),
        s.pg,
    );
    s.query(db, query)
}
