use super::*;
use export_doc_storage::{ReportTemplateQuery, TemplateAudience, TemplateVersionQuery};

fn audience(actor: &Actor) -> TemplateAudience<'_> {
    TemplateAudience {
        user_id: actor.id,
        company: &actor.company,
        department: &actor.department,
        administrator: actor.admin,
        can_view: auth::authorize(actor, PERMISSION, "view").is_ok(),
        shared: auth::scope_rank(actor, PERMISSION, "view") >= 2,
    }
}
fn paging(values: &[(&str, String)]) -> (usize, usize, i64) {
    let page = query(values, "pageNumber")
        .parse::<usize>()
        .unwrap_or(1)
        .max(1);
    let size = query(values, "pageSize")
        .parse::<usize>()
        .unwrap_or(30)
        .clamp(1, 200);
    let offset = i64::try_from(page.saturating_sub(1).saturating_mul(size)).unwrap_or(i64::MAX);
    (page, size, offset)
}
pub(super) fn list(
    service: &crate::engine::NativeService,
    actor: &Actor,
    values: &[(&str, String)],
) -> Result<Value> {
    let kind = report_type(query(values, "reportType"))?;
    demand_type(actor, kind)?;
    let keyword = query(values, "keyword");
    if keyword.chars().count() > 150 {
        return Err(invalid("模板搜索名称不能超过 150 个字符。"));
    }
    let keyword = store::normalize(keyword);
    let (page, size, offset) = paging(values);
    let (total, rows) =
        service
            .store
            .connection()?
            .query_report_templates(&ReportTemplateQuery {
                audience: audience(actor),
                report_type: kind,
                include_archived: query(values, "includeArchived") == "true",
                keyword: &keyword,
                exact_name: false,
                status: query(values, "status"),
                usable_only: false,
                offset,
                limit: size as i64,
            })?;
    let settings = service.store.settings("settings")?.unwrap_or_default();
    Ok(crate::contracts::page(
        rows.iter()
            .map(|v| record(actor, v, false, &settings))
            .collect(),
        total as usize,
        page,
        size,
    ))
}
pub(super) fn versions(
    service: &crate::engine::NativeService,
    actor: &Actor,
    id: i64,
    values: &[(&str, String)],
) -> Result<Value> {
    let connection = service.store.connection()?;
    let template = connection
        .report_template_metadata(id)?
        .ok_or_else(|| error(404, "记录不存在。"))?;
    visible(actor, &template)?;
    let (page, size, offset) = paging(values);
    let (total, mut rows) = connection.query_template_versions(&TemplateVersionQuery {
        kind: KIND,
        template_id: id,
        offset,
        limit: size as i64,
    })?;
    for row in &mut rows {
        if !row["versionNumber"].is_i64() {
            return Err(unavailable("模板历史内容损坏。"));
        }
        row.as_object_mut()
            .ok_or_else(|| unavailable("模板历史内容损坏。"))?
            .remove("templateId");
        row["userReportTemplateId"] = json!(id);
        row["canRestore"] = json!(
            template["shareScope"] == "Private" && policy::can_manage(actor, &template, "restore")
        );
    }
    Ok(crate::contracts::page(rows, total as usize, page, size))
}
pub(crate) fn usable(store: &store::Store, actor: &Actor, kind: &str) -> Result<Vec<Value>> {
    demand_type(actor, kind)?;
    let connection = store.connection()?;
    let mut rows = Vec::new();
    loop {
        crate::operation::check()?;
        let (total, page) = connection.query_report_templates(&ReportTemplateQuery {
            audience: audience(actor),
            report_type: kind,
            include_archived: false,
            keyword: "",
            exact_name: false,
            status: "",
            usable_only: true,
            offset: rows.len() as i64,
            limit: 200,
        })?;
        let finished = page.is_empty();
        rows.extend(page);
        if finished || rows.len() as i64 >= total {
            return Ok(rows);
        }
    }
}
