//! Indexed, permission-filtered metadata queries with bounded page materialization.
use super::*;

pub(super) fn query(
    service: &super::super::NativeService,
    actor: &Actor,
    query: &[(&str, String)],
) -> Result<Value> {
    auth::authorize(actor, "document.report-resources", "view")?;
    let _access = report_template_files::storage_read_lock(&service.paths)?;
    // Read managed containers outside SQL; authorize their references below
    // against the fresh actor while the container lock remains held.
    let managed = report_template_files::resource_types(&service.paths)?;
    service.store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        auth::authorize(&actor, "document.report-resources", "view")?;
        let (page, size, offset) = store::page_parameters(query);
        let keyword = store::normalize(crate::engine::request_values::find(query, "keyword").unwrap_or(""));
        let report_types: Vec<_> = ["ExportDocument", "PaymentVoucher"].into_iter()
            .filter(|kind| super::super::report_templates::demand_type(&actor, kind).is_ok()).collect();
        let managed_resources: Vec<_> = managed.iter().filter(|(_, kinds)| kinds.iter()
            .any(|kind| super::super::report_templates::policy::demand_catalog(&actor, kind).is_ok()))
            .map(|(id, _)| id.as_str()).collect();
        let (total, entries) = tx.query_resources(&export_doc_storage::ResourceQuery {
            actor: actor.id,
            administrator: actor.admin,
            invoices: auth::data_scope(&actor, "document.invoices", "view"),
            exporters: auth::data_scope(&actor, "document.master-data", "view"),
            templates: super::super::report_templates::queries::audience(&actor, false),
            output_templates: super::super::report_templates::queries::audience(&actor, true),
            report_types: &report_types,
            managed_resources: &managed_resources,
            keyword: &keyword,
            before_id: None,
            offset,
            limit: size as i64,
        })?;
        let targets: Vec<_> = entries.iter().filter_map(|entry| entry["resourceId"].as_str()).collect();
        let references = References::query(tx, &actor, None, Some(&targets))?;
        let items: Vec<_> = entries.iter().map(|entry| {
            let id = text(entry, "resourceId");
            let own = owns(&actor, entry);
            let referenced = references.all.contains(&id) || managed.contains_key(&id);
            json!({"id":id,"mediaType":entry["mediaType"],"byteLength":entry["byteLength"],"sha256":entry["sha256"],"ownsUpload":own,"isReferenced":referenced,"canRecycle":(actor.admin || own) && !referenced})
        }).collect();
        Ok(crate::contracts::page(items, total as usize, page, size))
    })
}
