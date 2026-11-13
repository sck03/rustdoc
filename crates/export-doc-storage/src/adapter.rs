//! Semantic operations shared by the SQLite and PostgreSQL adapters.
use super::*;
// The semantic boundary delegates simple operations to their owning modules.
macro_rules! semantic_operations {
    ($($name:ident($($arg:ident: $ty:ty),*) -> $result:ty => $module:ident::$target:ident;)+) => {
        $(fn $name(&self, $($arg: $ty),*) -> Result<$result> {
            $module::$target(self, $($arg),*)
        })+
    };
}
macro_rules! query_operations {
    ($($name:ident($query:ty) -> $result:ty => $module:ident::$build:ident, $execute:ident;)+) => {
        $(fn $name(&self, query: $query) -> Result<$result> {
            let (sql, projection) = $module::$build(query, self.provider() == "PostgreSQL")?;
            self.$execute(&sql, &projection, query.offset, query.limit)
        })+
    };
}
macro_rules! unsupported_operations {
    ($($name:ident($($arg:ident: $ty:ty),*) -> $result:ty => $message:literal;)+) => {
        $(fn $name(&self, $($arg: $ty),*) -> Result<$result> {
            Err(Error::new(ErrorKind::Unsupported, $message))
        })+
    };
}
/// All business operations use this semantic storage boundary, never provider SQL.
pub(crate) trait Adapter: Send {
    semantic_operations! {
        insert_file(file:&FileWrite<'_>)->() => file_catalog::insert;
        file_info(record:i64,kind:&str)->Option<FileInfo> => file_catalog::get;
        file_page(record:i64,prefix:&str,after:&str,limit:i64)->(i64,Vec<FileInfo>) => file_catalog::page;
        delete_files(record:i64,kind:Option<&str>)->() => file_catalog::remove;
        file_objects(after:&str,limit:i64,unused:bool)->Vec<FileObject> => file_catalog::objects;
        file_statistics()->Value => file_catalog::statistics;
        file_usage(company:&str)->u64 => file_catalog::usage;
        file_owner_company(record:i64)->String => file_catalog::owner_company;
        pending_file_copies(identity:&str)->Vec<FileObject> => file_catalog::pending_copies;
        locate_file(identity:&str,digest:&str)->() => file_catalog::locate;
        query_resources(query:&ResourceQuery<'_>)->(i64,Vec<Value>) => resource_index::catalogue;
        append_attachment_history(record:i64,kind:&str,body:&Value)->() => attachment_history::append;
        attachment_history(record:i64,kind:&str,before:Option<i64>,limit:i64)->Value => attachment_history::page;
        file_is_referenced(digest:&str)->bool => file_catalog::referenced;
        remove_unused_file(digest:&str)->() => file_catalog::remove_unused;
        email_template_name_exists(owner:i64,id:i64,name:&str,category:&str) -> bool => template_query::email_name_exists;
        scan_hs(query: &HsQuery<'_>, after: Option<&Value>) -> (Option<Value>, Vec<Value>) => hs_query::scan;
        access_restrictions(kind: &str, id: i64, resolved: bool) -> Vec<Value> => access_scope::list;
        save_access_restriction(kind: &str, id: i64, body: &Value) -> Value => access_scope::save;
        permission_scheme_versions(scheme: i64, offset: i64, limit: i64) -> Value => permission_versions::versions;
        permission_scheme_version(scheme: i64, version: i64) -> Option<Value> => permission_versions::publication;
        publish_permission_scheme(scheme: i64, body: &Value, actor: i64, time: &str) -> i64 => permission_versions::publish;
        foundation_candidates(unit: i64, keyword: &str, date: &str, offset: i64, limit: i64) -> Value => foundation::candidates;
        foundation_list(kind: &str, unit: i64, scope: &DataScope<'_>, offset: i64, limit: i64) -> Value => foundation::list;
        foundation_appointment(unit: i64, key: &str) -> Option<Value> => foundation::appointment;
        foundation_get(kind: &str, unit: i64, id: i64) -> Option<Value> => foundation::get;
        foundation_save(kind: &str, id: Option<i64>, unit: i64, expected: i64, body: &Value) -> Value => foundation::save;
        responsibility_appointments(unit: i64, responsibility: i64, project: Option<i64>, kind: &str, date: &str) -> Vec<Value> => foundation::resolve_duty;
        placement_appointments(unit: i64, source: &str, id: i64, date: &str) -> Vec<Value> => foundation::placement_appointments;
        workflow_version_next(company: &str, key: &str) -> i64 => workflow_publications::next;
        single_window_submission_version(source: i64, business: &str) -> i64 => single_window_query::latest_version;
        workflow_versions(company: &str, key: &str, offset: i64, limit: i64) -> Value => workflow_publications::versions;
        workflow_activation_save(entry: &Value) -> Value => workflow_publications::save;
        workflow_activation_list(company: &str, due: Option<&str>) -> Vec<Value> => workflow_publications::list;
        workflow_activation_state(id: i64, expected: i64, state: &str, error: &str) -> bool => workflow_publications::state;
        workflow_activation_receipt(actor: i64, context: &str, key: &str) -> Option<Value> => workflow_publications::receipt;
        foundation_catalog(kind: &str, unit: i64, offset: i64, limit: i64) -> Value => foundation::catalog;
        foundation_project_options(unit: i64, appointment: &str, day: &str, offset: i64, limit: i64) -> Value => foundation::project_options;
        due_personnel_orders(now: &str) -> Vec<Value> => workflow_schedule::due;
        due_workflow_nodes(now: &str) -> Vec<Value> => workflow_schedule::due_nodes;
        schedule_personnel_order(entry: &Value) -> () => workflow_schedule::schedule;
        personnel_order_state(id: i64, state: &str, error: &str) -> () => workflow_schedule::state;
        personnel_order(id: i64) -> Option<Value> => workflow_schedule::order;
        command_receipt(actor: i64, context: &str, key: &str) -> Option<Value> => command_receipts::get;
        put_command_receipt(receipt: &Value, result: &Value) -> () => command_receipts::put;
        append_workflow_operation(entry: &Value) -> () => command_receipts::operation;
        freeze_request_revision(id: i64, snapshot: &Value) -> () => workflow_runtime::freeze;
        save_workflow_state(request: i64, revision: i64, progress: &WorkflowProgress<'_>, plan: &Value, active_tasks: &[String]) -> () => workflow_runtime::persist;
        query_record_fields(query: &GenericQuery<'_>, search_fields: &[&str], count: bool) -> (i64, Vec<Value>) => record_selection::page;
        query_ordered_record_fields(query: &GenericQuery<'_>, search_fields: &[&str], order: RecordOrder) -> (i64, Vec<Value>) => record_selection::ordered_page;
        export_primary_contacts(kind: &str, scope: &DataScope<'_>, relation: &str, parents: &[i64]) -> Vec<Value> => record_selection::contacts;
    }
    query_operations! {
        query_hs(&HsQuery<'_>) -> Vec<Value> => hs_query::build, query_slice;
        page_hs(&HsQuery<'_>) -> (i64,Vec<Value>) => hs_query::build, query_page;
        query_generic(&GenericQuery<'_>) -> (i64, Vec<Value>) => generic_query::build, query_page;
        query_business(&BusinessQuery<'_>) -> (i64, Vec<Value>) => business_query::build, query_page;
        query_crm(&CrmQuery<'_>) -> (i64, Vec<Value>) => crm_query::build, query_page;
        query_related(&RelatedQuery<'_>) -> (i64, Vec<Value>) => related_query::build, query_page;
        query_personnel(&PersonnelQuery<'_>) -> (i64, Vec<Value>) => personnel_query::build, query_page;
    }
    unsupported_operations! {
        query_json(_statement: &str, _values: &[String]) -> Value => "存储适配器不支持专用聚合查询。";
        query_audits(_query: &AuditQuery<'_>) -> (i64, Vec<Value>) => "存储适配器不支持审计筛选分页。";
        attachment_category_counts(_company: &str) -> std::collections::BTreeMap<i64, i64> => "存储适配器不支持附件分类统计。";
        import_preview_usage(_owner: i64) -> ImportPreviewUsage => "存储适配器不支持导入预检容量查询。";
        query_slice(_sql: &QuerySql, _projection: &str, _offset: i64, _limit: i64) -> Vec<Value> => "存储适配器不支持有界扫描。";
        job_output_bytes() -> u64 => "存储适配器不支持任务容量查询。";
        explain_statement(_statement: &str, _values: &[String]) -> Value => "存储适配器不支持查询计划。";
        blob_info(_record_id: i64, _kind: &str) -> Option<BlobInfo> => "存储适配器不支持文件元数据查询。";
        backup(_path: &Path) -> () => "该数据库应使用对应的数据库备份模块。";
        verify_backup(_path: &Path) -> () => "该数据库应使用对应的数据库备份模块。";
        restore(_path: &Path) -> () => "该数据库应使用对应的数据库恢复模块。";
    }

    fn explain_query(&self, query: &QueryPlan<'_>) -> Result<Value> {
        let (statement, values) = query.statement(self.provider() == "PostgreSQL")?;
        self.explain_statement(&statement, &values)
    }

    fn records_by_ids(
        &self,
        kind: &str,
        ids: &[i64],
        fields: &[(&str, &str)],
    ) -> Result<Vec<Value>> {
        crate::record_selection::by_keys(
            self,
            kind,
            true,
            &ids.iter().map(i64::to_string).collect::<Vec<_>>(),
            fields,
        )
    }
    fn records_by_identities(
        &self,
        kind: &str,
        identities: &[String],
        fields: &[(&str, &str)],
    ) -> Result<Vec<Value>> {
        crate::record_selection::by_keys(self, kind, false, identities, fields)
    }

    fn unit_accounts(&self, company: &str, date: &str, all: bool) -> Result<Vec<Value>> {
        let mut result = vec![];
        let mut cursor = None;
        loop {
            operation::check()?;
            let rows = self.unit_account_page(company, date, all, cursor, 200)?;
            if rows.is_empty() {
                break;
            }
            cursor = rows.last().and_then(|row| row["id"].as_i64());
            if cursor.is_none() {
                return Err(Error::unavailable("账号游标损坏。"));
            }
            result.extend(rows);
        }
        Ok(result)
    }
    fn unit_account_page(
        &self,
        company: &str,
        date: &str,
        all: bool,
        before: Option<i64>,
        limit: i64,
    ) -> Result<Vec<Value>> {
        personnel_links::account_page(self, company, date, all, false, before, limit)
    }
    fn workflow_notification_account_page(
        &self,
        company: &str,
        date: &str,
        before: Option<i64>,
        limit: i64,
    ) -> Result<Vec<Value>> {
        personnel_links::account_page(self, company, date, false, true, before, limit)
    }
    fn resource_has_open_requests(&self, kind: &str, parent: i64, states: &[&str]) -> Result<bool> {
        let pg = self.provider() == "PostgreSQL";
        Ok(!self
            .query_slice(
                &resource_open_query::build(kind, parent, states, pg)?,
                &sql::projection(&[("id", "id")], pg)?,
                0,
                1,
            )?
            .is_empty())
    }

    fn supplier_overview(&self, scope: &DataScope<'_>) -> Result<Value> {
        let (statement, values) =
            supplier_overview_query::build(scope, self.provider() == "PostgreSQL");
        let result = self.query_json(&statement, &values)?;
        if result["invalidCount"]
            .as_i64()
            .is_none_or(|count| count != 0)
        {
            return Err(Error::unavailable("供应商评价缺少有效所属编号。"));
        }
        Ok(result)
    }
    fn execute_statement(&self, statement: &str, values: &[String]) -> Result<u64>;

    fn workflow_system_requests(&self, action: &str) -> Result<Vec<Value>> {
        crate::workflow_schedule::systems(self, action)
    }

    fn workflow_exceptions(&self, company: &str, offset: i64, limit: i64) -> Result<Value> {
        crate::workflow_schedule::exceptions(self, company, offset, limit)
    }

    fn query_ordered_records(
        &self,
        query: &GenericQuery<'_>,
        order: RecordOrder,
    ) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) =
            ordered_query::build(query, order, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    fn record_exists(&self, kind: &str, filters: &[(&str, String)]) -> Result<bool> {
        let query = GenericQuery {
            kind,
            scope: DataScope {
                all: true,
                ..Default::default()
            },
            filters,
            fields: &[("id", "id")],
            limit: 1,
            ..Default::default()
        };
        let (sql, projection) = generic_query::build(&query, self.provider() == "PostgreSQL")?;
        Ok(!self.query_slice(&sql, &projection, 0, 1)?.is_empty())
    }
    fn organization_referenced(
        &self,
        reference: OrganizationReference,
        code: &str,
        company: &str,
        exclude: i64,
        kinds: &[&str],
    ) -> Result<bool> {
        let pg = self.provider() == "PostgreSQL";
        let sql = organization_query::build(reference, code, company, exclude, kinds, pg);
        Ok(!self
            .query_slice(&sql, &sql::projection(&[("id", "id")], pg)?, 0, 1)?
            .is_empty())
    }
    fn personnel_exists(&self, query: &PersonnelQuery<'_>) -> Result<bool> {
        let pg = self.provider() == "PostgreSQL";
        let (sql, _) = personnel_query::build(query, pg)?;
        Ok(!self
            .query_slice(&sql, &sql::projection(&[("id", "id")], pg)?, 0, 1)?
            .is_empty())
    }
    fn personnel_for_accounts(&self, users: &[i64], fields: &[(&str, &str)]) -> Result<Vec<Value>> {
        let mut result = vec![];
        for ids in users.chunks(200) {
            operation::check()?;
            let (query, projection) =
                personnel_links::accounts(ids, fields, self.provider() == "PostgreSQL")?;
            result.extend(self.query_slice(&query, &projection, 0, ids.len() as i64)?);
        }
        Ok(result)
    }
    fn appointment_references(&self, people: &[i64]) -> Result<Vec<Value>> {
        let mut result = vec![];
        for ids in people.chunks(200) {
            operation::check()?;
            let (query, projection) =
                personnel_links::appointments(ids, self.provider() == "PostgreSQL")?;
            result.extend(self.query_slice(&query, &projection, 0, ids.len() as i64)?);
        }
        Ok(result)
    }

    fn expired_import_previews(&self, before: &str) -> Result<Vec<Value>> {
        let pg = self.provider() == "PostgreSQL";
        self.query_slice(
            &import_preview_query::expired(before, pg),
            &sql::projection(
                &[
                    ("id", "id"),
                    ("versionNumber", "versionNumber"),
                    ("expiresAt", "expiresAt"),
                ],
                pg,
            )?,
            0,
            200,
        )
    }

    fn is_closed(&self) -> bool {
        false
    }
    fn query_jobs(&self, query: &JobQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        if query.retention.is_some() {
            let (invalid, _) = self.query_page(
                &job_query::invalid_times(pg),
                if pg { "r.body::text" } else { "r.body" },
                0,
                1,
            )?;
            if invalid > 0 {
                return Err(Error::unavailable(
                    "文件任务时间或所有者损坏，已停止自动清理。",
                ));
            }
        }
        let (sql, projection) = job_query::build(query, pg)?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    fn job_counts(&self) -> Result<Value>;
    fn expired_job_outputs(&self, policy: &JobOutputRetention) -> Result<Vec<Value>> {
        let pg = self.provider() == "PostgreSQL";
        self.query_slice(
            &job_query::expired_outputs(policy, pg)?,
            &sql::projection(&[("id", "id")], pg)?,
            0,
            200,
        )
    }
    fn expired_job_inputs(&self, policy: &JobOutputRetention) -> Result<Vec<Value>> {
        let pg = self.provider() == "PostgreSQL";
        self.query_slice(
            &job_query::expired_inputs(policy, pg)?,
            &sql::projection(&[("id", "id")], pg)?,
            0,
            200,
        )
    }
    fn job_input_bytes(&self) -> Result<u64>;

    /// Development diagnostics use the exact production statement and bindings.
    /// This does not expose arbitrary SQL through any HTTP endpoint.

    /// Descending keyset scan without a count. The cursor uses the physical
    /// record id even when the caller projects fields that omit the DTO id.
    fn scan_generic(
        &self,
        query: &GenericQuery<'_>,
        before_id: Option<i64>,
    ) -> Result<(Option<i64>, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        let (sql, projection) = generic_query::build(query, pg)?;
        self.scan_page(sql, &projection, before_id, query.limit)
    }
    fn scan_worklist(
        &self,
        query: &ScopedQuery<'_>,
        actor: i64,
        customer_scope: &DataScope<'_>,
        scan: &WorklistScan<'_>,
        cursor: Option<i64>,
    ) -> Result<(Option<i64>, Vec<Value>)> {
        let (sql, projection) = worklist_query::build(
            query,
            actor,
            customer_scope,
            scan,
            self.provider() == "PostgreSQL",
        )?;
        self.scan_ordered_page(sql, &projection, cursor, query.limit, scan.ascending)
    }
    fn scan_records(
        &self,
        query: &RecordQuery<'_>,
        before_id: Option<i64>,
    ) -> Result<(Option<i64>, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        let mut statement = record_query::sql(query, pg);
        if workflow_runtime::request_kind(query.kind) {
            statement.source = query_source::Source::Workflow;
        }
        self.scan_page(
            statement,
            &sql::projection(query.fields, pg)?,
            before_id,
            query.limit,
        )
    }
    fn scan_page(
        &self,
        sql: QuerySql,
        projection: &str,
        before_id: Option<i64>,
        limit: i64,
    ) -> Result<(Option<i64>, Vec<Value>)> {
        self.scan_ordered_page(sql, projection, before_id, limit, false)
    }
    fn scan_ordered_page(
        &self,
        mut sql: QuerySql,
        projection: &str,
        previous: Option<i64>,
        limit: i64,
        ascending: bool,
    ) -> Result<(Option<i64>, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        if let Some(id) = previous {
            let cursor = sql::bind(&mut sql.values, id, pg);
            let comparison = if ascending { ">" } else { "<" };
            sql.filter
                .push_str(&format!(" AND r.id{comparison}CAST({cursor} AS BIGINT)"));
        }
        sql.order = if ascending { "r.id ASC" } else { "r.id DESC" }.into();
        let projection = if pg {
            format!("jsonb_build_array(r.id,({projection})::jsonb)::text")
        } else {
            format!("json_array(r.id,json({projection}))")
        };
        let mut cursor = previous;
        let mut rows = Vec::new();
        for mut row in self.query_slice(&sql, &projection, 0, limit)? {
            let parts = row
                .as_array_mut()
                .filter(|parts| parts.len() == 2)
                .ok_or_else(|| Error::unavailable("记录扫描结果损坏。"))?;
            let id = parts[0]
                .as_i64()
                .filter(|id| {
                    *id > 0
                        && cursor.is_none_or(|cursor| {
                            if ascending {
                                *id > cursor
                            } else {
                                *id < cursor
                            }
                        })
                })
                .ok_or_else(|| Error::unavailable("记录扫描游标无效。"))?;
            cursor = Some(id);
            rows.push(parts.pop().expect("validated row projection"));
        }
        Ok((cursor, rows))
    }
    fn resource_references(
        &self,
        kind: &str,
        ids: Option<&[&str]>,
        before_id: Option<i64>,
        limit: i64,
    ) -> Result<(Option<i64>, Vec<Value>)> {
        let (sql, projection) = resource_query::build(kind, ids, self.provider() == "PostgreSQL")?;
        self.scan_page(sql, &projection, before_id, limit.clamp(1, 200))
    }
    fn ownership_counts(&self, kinds: &[&str]) -> Result<Vec<(Option<i64>, i64)>> {
        crate::ownership_query::counts(self, kinds)
    }

    fn scan_crm(&self, query: &CrmQuery<'_>, after_id: i64) -> Result<Vec<Value>> {
        let (mut sql, projection) = crm_query::build(query, self.provider() == "PostgreSQL")?;
        let cursor = sql::bind(&mut sql.values, after_id, self.provider() == "PostgreSQL");
        sql.filter
            .push_str(&format!(" AND r.id>CAST({cursor} AS BIGINT)"));
        sql.order = "r.id".into();
        self.query_slice(&sql, &projection, 0, query.limit)
    }

    fn query_office(&self, query: &OfficeQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        let projection = if query.fields.is_empty() {
            office_query::projection(query.kind, pg)
        } else {
            sql::projection(query.fields, pg)?
        };
        self.query_page(
            &office_query::build(query, pg)?,
            &projection,
            query.offset,
            query.limit,
        )
    }
    fn provider(&self) -> &'static str;
    fn health(&self) -> Result<()>;
    fn begin(&self) -> Result<()>;
    fn begin_read(&self) -> Result<()>;
    fn commit(&self) -> Result<()>;
    fn rollback(&self) -> Result<()>;
    fn checkpoint(&self) -> Result<()>;
    fn get(&self, kind: &str, id: i64) -> Result<Option<Value>>;
    fn find_identity(&self, kind: &str, identity: &str) -> Result<Option<Value>>;
    fn all(&self, kind: &str) -> Result<Vec<Value>>;
    fn query_records(&self, query: &RecordQuery<'_>) -> Result<(i64, Vec<Value>)>;

    fn query_workflow_tasks(&self, query: &WorkflowTaskQuery<'_>) -> Result<(i64, Vec<Value>)>;
    fn workflow_decisions(
        &self,
        request: i64,
        revision: i64,
        generation: Option<i64>,
        offset: i64,
        limit: i64,
    ) -> Result<Value> {
        crate::workflow_decisions::page(self, request, revision, generation, offset, limit)
    }
    fn query_workflow_summary(&self, query: &WorkflowSummaryQuery<'_>)
    -> Result<(i64, Vec<Value>)>;
    fn query_page(
        &self,
        sql: &QuerySql,
        projection: &str,
        offset: i64,
        limit: i64,
    ) -> Result<(i64, Vec<Value>)>;
    fn query_report_templates(&self, query: &ReportTemplateQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        self.query_page(
            &template_query::catalog(query, pg),
            &template_query::metadata(pg, false),
            query.offset,
            query.limit,
        )
    }
    fn report_template_metadata(&self, id: i64) -> Result<Option<Value>> {
        let pg = self.provider() == "PostgreSQL";
        let sql = QuerySql {
            source: crate::query_source::Source::Projected,
            filter: format!("r.kind='report-templates' AND r.id={id}"),
            order: "r.id".into(),
            values: vec![],
        };
        Ok(self
            .query_page(&sql, &template_query::metadata(pg, false), 0, 1)?
            .1
            .pop())
    }
    fn query_template_versions(
        &self,
        query: &TemplateVersionQuery<'_>,
    ) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        self.query_page(
            &template_query::versions(query, pg),
            &template_query::metadata(pg, true),
            query.offset,
            query.limit,
        )
    }
    fn template_history_usage(
        &self,
        query: &TemplateVersionQuery<'_>,
        keep: i64,
    ) -> Result<TemplateHistoryUsage> {
        let (sql, projection) =
            template_history_query::usage(query, keep, self.provider() == "PostgreSQL");
        let value = self.query_json(
            &crate::query_source::aggregate(&sql, &projection),
            &sql.values,
        )?;
        TemplateHistoryUsage::from_value(&value)
    }
    fn template_history_candidates(&self, query: &TemplateVersionQuery<'_>) -> Result<Vec<Value>> {
        let (sql, projection) =
            template_history_query::candidates(query, self.provider() == "PostgreSQL");
        self.query_slice(&sql, &projection, query.offset, query.limit)
    }
    fn query_email_templates(&self, query: &EmailTemplateQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        self.query_page(
            &template_query::email_catalog(query, pg),
            if pg { "r.body::text" } else { "r.body" },
            query.offset,
            query.limit,
        )
    }
    fn template_version(&self, kind: &str, id: i64, version: i64) -> Result<Option<Value>> {
        let pg = self.provider() == "PostgreSQL";
        let mut sql = template_query::versions(
            &TemplateVersionQuery {
                kind,
                template_id: id,
                offset: 0,
                limit: 1,
            },
            pg,
        );
        sql.filter.push_str(&format!(
            " AND CAST({} AS BIGINT)={version}",
            template_query::field("content.versionNumber", pg)
        ));
        Ok(self
            .query_page(&sql, if pg { "r.body::text" } else { "r.body" }, 0, 1)?
            .1
            .pop())
    }
    fn query_communications(&self, query: &CommunicationQuery<'_>) -> Result<(i64, Vec<Value>)>;
    fn insert(&self, record: &RecordWrite<'_>) -> Result<i64>;
    fn personnel_private_profile(&self, id: i64) -> Result<Option<Value>>;
    fn set_personnel_private_profile(&self, id: i64, body: &Value) -> Result<()>;
    fn update(&self, id: i64, expected: i64, record: &RecordWrite<'_>) -> Result<bool>;
    fn set_body(&self, id: i64, body: &Value) -> Result<()>;
    fn delete(&self, kind: &str, id: i64, expected: i64) -> Result<bool>;
    fn append_audit_details(&self, audit: &AuditWrite<'_>, details: &Value) -> Result<()>;
    fn audit_events(&self, before_id: i64, limit: i64) -> Result<Vec<Value>>;
    fn delete_audits(&self, ids: &[i64]) -> Result<usize>;
    fn history(&self, kind: Option<&str>, id: Option<i64>) -> Result<Vec<Value>>;
    fn has_history_except(&self, kind: &str, id: i64, allowed: &[&str]) -> Result<bool>;
    fn credential(&self, id: i64) -> Result<Option<Credential>>;
    fn set_credential(&self, id: i64, salt: &[u8], hash: &[u8], iterations: u32) -> Result<()>;
    fn settings(&self, name: &str) -> Result<Option<Value>>;
    fn settings_revision(&self, name: &str) -> Result<i64>;
    fn compare_set_settings(&self, name: &str, expected: i64, body: &Value) -> Result<bool>;
    fn set_settings(&self, name: &str, version: i64, body: &Value) -> Result<()>;
    fn attachment_bytes(&self, invoice_id: i64) -> Result<i64>;
    fn insert_blob(&self, blob: &BlobWrite<'_>) -> Result<()>;
    fn blob(&self, record_id: i64, kind: &str) -> Result<Option<Blob>>;

    fn delete_blobs(&self, record_id: i64) -> Result<()>;
    fn delete_blob(&self, record_id: i64, kind: &str) -> Result<()>;
}
