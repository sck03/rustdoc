//! Single native API definition. The served document and all clients are projections.
mod model;
mod sheet_fields;
pub use sheet_fields::{DETAIL_FIELDS, SHEET_FIELDS};
mod catalogue_dependencies;
mod catalogue_editions;
mod catalogue_modules;
mod catalogue_resources;
mod catalogue_roles;
mod endpoints_audit_logs;
mod endpoints_auth;
mod endpoints_backup;
mod endpoints_business_attachment_categories;
mod endpoints_business_attachments;
mod endpoints_crm;
mod endpoints_custom_options;
mod endpoints_dashboard;
mod endpoints_diagnostics;
mod endpoints_disaster_recovery;
mod endpoints_email_templates;
mod endpoints_invoices;
mod endpoints_jobs;
mod endpoints_master_data;
mod endpoints_office;
mod endpoints_organization;
mod endpoints_organization_directory;
mod endpoints_payments;
mod endpoints_permission_templates;
mod endpoints_postgresql_maintenance;
mod endpoints_query;
mod endpoints_reports;
mod endpoints_server_migration;
mod endpoints_session;
mod endpoints_settings;
mod endpoints_shared_database;
mod endpoints_single_window;
mod endpoints_suppliers;
mod endpoints_support_package;
mod endpoints_system;
mod endpoints_tools;
mod endpoints_users;
mod endpoints_worklist;
mod file_storage;
mod metadata;
mod metadata_configuration;
mod metadata_permissions;
mod metadata_values;
mod policies;
mod responses;
mod route_parameters;
mod scheduler_metrics;
mod schema_shapes;
mod schemas_hs_1;
mod schemas_invoices_1;
mod schemas_maintenance_1;
mod schemas_oa_forms;
mod schemas_personnel_1;
mod schemas_platform_1;
mod schemas_platform_2;
mod schemas_reports_1;
mod schemas_sales_1;
mod schemas_single_window_1;
mod schemas_workflow_1;
mod single_window_sources;
use serde_json::Value;
use std::collections::BTreeMap;

pub(super) fn document() -> Value {
    let mut schemas = Vec::new();
    schemas.extend(file_storage::schemas());
    schemas.extend(schemas_platform_1::entries());
    schemas.extend(schemas_platform_2::entries());
    schemas.extend(schemas_invoices_1::entries());
    schemas.extend(schemas_maintenance_1::entries());
    schemas.extend(schemas_sales_1::entries());
    schemas.extend(schemas_single_window_1::entries());
    schemas.extend(single_window_sources::schemas());
    schemas.extend(schemas_reports_1::entries());
    schemas.extend(schemas_hs_1::entries());
    schemas.extend(schemas_personnel_1::entries());
    schemas.extend(schemas_workflow_1::entries());
    schemas.extend(schemas_oa_forms::entries());
    let schemas: BTreeMap<_, _> = schemas.into_iter().collect();
    let mut endpoints = Vec::new();
    endpoints.extend(file_storage::endpoints());
    endpoints.extend(endpoints_session::entries());
    endpoints.extend(endpoints_system::entries());
    endpoints.extend(endpoints_business_attachments::entries());
    endpoints.extend(endpoints_invoices::entries());
    endpoints.extend(endpoints_business_attachment_categories::entries());
    endpoints.extend(endpoints_worklist::entries());
    endpoints.extend(endpoints_office::entries());
    endpoints.extend(endpoints_organization::entries());
    endpoints.extend(endpoints_auth::entries());
    endpoints.extend(endpoints_users::entries());
    endpoints.extend(endpoints_organization_directory::entries());
    endpoints.extend(endpoints_permission_templates::entries());
    endpoints.extend(endpoints_settings::entries());
    endpoints.extend(endpoints_backup::entries());
    endpoints.extend(endpoints_postgresql_maintenance::entries());
    endpoints.extend(endpoints_shared_database::entries());
    endpoints.extend(endpoints_support_package::entries());
    endpoints.extend(endpoints_server_migration::entries());
    endpoints.extend(endpoints_dashboard::entries());
    endpoints.extend(endpoints_query::entries());
    endpoints.extend(endpoints_payments::entries());
    endpoints.extend(endpoints_audit_logs::entries());
    endpoints.extend(endpoints_jobs::entries());
    endpoints.extend(endpoints_custom_options::entries());
    endpoints.extend(endpoints_tools::entries());
    endpoints.extend(endpoints_reports::entries());
    endpoints.extend(endpoints_single_window::entries());
    endpoints.extend(single_window_sources::endpoints());
    endpoints.extend(endpoints_master_data::entries());
    endpoints.extend(endpoints_crm::entries());
    endpoints.extend(endpoints_suppliers::entries());
    endpoints.extend(endpoints_email_templates::entries());
    endpoints.extend(endpoints_diagnostics::entries());
    endpoints.extend(endpoints_disaster_recovery::entries());
    let mut paths = BTreeMap::<&str, BTreeMap<&str, model::Operation>>::new();
    for mut endpoint in endpoints {
        if matches!(
            endpoint.operation.id,
            "UploadBusinessAttachment" | "UploadPersonnelAttachment"
        ) || endpoint
            .operation
            .office
            .as_ref()
            .is_some_and(|capability| capability.action == "upload")
            || endpoint
                .operation
                .communication
                .as_ref()
                .is_some_and(|capability| capability.action == "upload")
        {
            endpoint.operation.timeout_seconds = crate::request_limits::TRANSFER_SECONDS;
            if let Some(body) = &mut endpoint.operation.body {
                body.max_bytes = Some(100 * 1024 * 1024);
            }
        }
        if endpoint.operation.id == "GetBusinessAttachment" {
            for name in ["revisionBefore", "eventBefore"] {
                endpoint
                    .operation
                    .parameters
                    .get_or_insert_with(Vec::new)
                    .push(model::p(
                        name,
                        "query",
                        None,
                        model::i().format("int64").minimum(model::num("1")),
                    ));
            }
        }
        if endpoint.method == "get" {
            if let Some(response) = endpoint
                .operation
                .responses
                .get("200")
                .filter(|response| {
                    response
                        .content
                        .as_ref()
                        .is_some_and(|content| content.keys().any(|mime| !mime.contains("json")))
                })
                .cloned()
            {
                endpoint.operation.responses.insert(
                    "206",
                    model::Response {
                        description: "Partial Content",
                        ..response
                    },
                );
                let (status, response) = responses::error("416");
                endpoint.operation.responses.insert(status, response);
                endpoint
                    .operation
                    .parameters
                    .get_or_insert_with(Vec::new)
                    .push(model::p("Range", "header", None, model::s()));
            }
        }
        assert!(
            paths
                .entry(endpoint.path)
                .or_default()
                .insert(endpoint.method, endpoint.operation)
                .is_none(),
            "duplicate native endpoint"
        );
    }
    let mut document = metadata::root();
    document["components"]["schemas"] = serde_json::to_value(schemas).expect("native schemas");
    document["paths"] = serde_json::to_value(paths).expect("native operations");
    document
}
