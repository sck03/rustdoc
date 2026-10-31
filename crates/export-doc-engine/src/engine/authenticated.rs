//! Authenticated application dispatch, separate from host lifecycle.
use super::*;
impl NativeService {
    pub(super) fn authenticated(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        query: &[(&str, String)],
        body_value: Value,
        actor: &Actor,
        token: &str,
    ) -> Result<Value> {
        if let Some(result) = units::dispatch(
            &self.store,
            actor,
            operation,
            parameters,
            query,
            &body_value,
        ) {
            return result;
        }
        match operation {
            LOGOUT => {
                self.sessions.logout(token)?;
                Ok(json!({"success":true}))
            }
            GET_CURRENT_USER => auth::user_dto_for(
                &self.store,
                &self.store.get("users", actor.id)?,
                &self.clock,
                actor,
            ),
            RENEW_SESSION => self.sessions.renew(&self.store, token, &self.clock),
            SWITCH_UNIT_CONTEXT => self.sessions.replace_context(
                &self.store,
                token,
                &self.clock,
                &records::text(&body_value, "contextId"),
            ),
            GET_PERSONNEL_OPTIONS => personnel_queries::options(&self.store, actor, query),
            LIST_PERSONNEL_REPORTING_OPTIONS => {
                personnel_organization::reporting_options(&self.store, actor, query)
            }
            LIST_PERSONNEL => personnel_queries::directory(&self.store, actor, &self.clock, query),
            GET_PERSONNEL => personnel_queries::read(&self.store, actor, records::id(parameters)?),
            GET_PERSONNEL_HISTORY => {
                personnel_queries::history(&self.store, actor, records::id(parameters)?, query)
            }
            GET_MEETING_ROOM_AVAILABILITY => {
                office_queries::availability(&self.store, actor, records::id(parameters)?, query)
            }
            GET_REPORT_TEMPLATE_FIELD_CATALOG => {
                auth::authorize(actor, "document.report-templates", "view")?;
                let kind = query
                    .iter()
                    .find(|(key, _)| *key == "reportType")
                    .map(|(_, value)| value.as_str())
                    .unwrap_or("ExportDocument");
                report_templates::demand_type(actor, kind)?;
                serde_json::to_value(report_templates::fields(kind)?).map_err(Into::into)
            }
            GET_REPORT_TEMPLATE_V3_CONTRACT => report_templates::contract(),
            LIST_JOBS => self.jobs.list(actor, query),
            CLEAR_FINISHED_JOBS => self.jobs.clear_finished(actor),
            LIST_DATABASE_BACKUPS => maintenance::list(&self.store, &self.paths, actor),
            CLEANUP_DATABASE_BACKUPS => {
                maintenance::cleanup(&self.store, &self.paths, actor, &body_value)
            }
            CREATE_DATABASE_BACKUP => {
                let path = maintenance::create(&self.store, &self.paths, actor)?;
                self.jobs.completed(
                    actor,
                    "DatabaseBackup",
                    "数据库备份",
                    &format!("备份已保存：{}", path.display()),
                )
            }
            RESTORE_DATABASE_BACKUP => {
                self.jobs.ensure_idle()?;
                maintenance::restore(&self.store, &self.paths, actor, &body_value)?;
                self.sessions.clear()?;
                self.jobs.recover()?;
                self.backup_scheduler.refresh(self)?;
                #[cfg(feature = "exchange-rates")]
                self.exchange_scheduler.refresh(self)?;
                let job = self.jobs.completed(
                    actor,
                    "DatabaseRestore",
                    "数据库恢复",
                    "恢复完成，请重新登录",
                )?;
                Ok(job)
            }
            ANALYZE_INVOICE_PROFIT => {
                auth::authorize(actor, "document.invoices", "view")?;
                workflows::profit(&body_value)
            }
            GET_DASHBOARD => dashboard::query(&self.store, actor, &self.clock),
            GET_CRM_DASHBOARD => crm_dashboard::query(&self.store, actor, &self.clock),
            GET_SUPPLIER_ASSESSMENT_OVERVIEW => supplier_overview::query(&self.store, actor),
            GET_WORKLIST => worklist::query(&self.store, actor, &self.clock, query),
            LIST_QUERIED_INVOICES => invoice_query::list(&self.store, actor, query),
            REVIEW_INVOICE => {
                auth::authorize(actor, "document.invoices", "view")?;
                workflows::review(&body_value)
            }
            GET_MEETING_BOOKING_HISTORY
            | GET_OFFICE_SUPPLY_REQUEST_HISTORY
            | GET_OFFICE_STOCK_HISTORY => office_events::history(
                &self.store,
                actor,
                operation,
                records::id(parameters)?,
                query,
            ),
            LIST_INVOICE_STATUS_HISTORY => {
                let kind = "invoices";
                let resource = catalog::resource(kind).unwrap();
                auth::authorize(actor, resource.permission, "view")?;
                let id = records::id(parameters)?;
                let record = self.store.get(kind, id)?;
                if !auth::visible(actor, resource.permission, "view", &record) {
                    return Err(error(403, "没有读取历史的权限。"));
                }
                Ok(json!(store::history(&self.store.connection()?, kind, id)?))
            }
            GET_SETTINGS => settings::read(&self.store, actor),
            UPDATE_SETTINGS | VALIDATE_SETTINGS => {
                let response = settings::save(
                    &self.store,
                    &self.paths,
                    &self.protector,
                    actor,
                    operation,
                    &body_value,
                )?;
                #[cfg(feature = "exchange-rates")]
                let response = exchange::settings_saved(self, operation, response);
                Ok(response)
            }
            LIST_CUSTOM_OPTIONS | SAVE_CUSTOM_OPTION => {
                custom_options::handle(&self.store, actor, operation, parameters, &body_value)
            }
            LIST_CUSTOMERS | LIST_EXPORTERS | LIST_PAYEES | LIST_UNITS | LIST_PORTS => {
                let kind = match operation {
                    LIST_CUSTOMERS => "customers",
                    LIST_EXPORTERS => "exporters",
                    LIST_PAYEES => "payees",
                    LIST_UNITS => "units",
                    _ => "ports",
                };
                records::list(
                    &self.store,
                    actor,
                    catalog::resource(kind).unwrap(),
                    operation,
                    query,
                    parameters,
                )
            }
            _ => {
                let resource = records::find(operation)
                    .ok_or_else(|| unsupported("此项原生操作尚未接入。"))?;
                records::handle(
                    &self.clock,
                    &self.store,
                    actor,
                    resource,
                    operation,
                    parameters,
                    query,
                    Some(body_value),
                )
            }
        }
    }
}
