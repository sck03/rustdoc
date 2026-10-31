//! Semantic operations shared by the SQLite and PostgreSQL adapters.
use super::*;
/// All business operations use this semantic storage boundary, never provider SQL.
pub(crate) trait Adapter: Send {
    fn access_restrictions(&self, kind: &str, id: i64, resolved: bool) -> Result<Vec<Value>> {
        access_scope::list(self, kind, id, resolved)
    }
    fn save_access_restriction(&self, kind: &str, id: i64, body: &Value) -> Result<Value> {
        access_scope::save(self, kind, id, body)
    }
    fn permission_scheme_versions(&self, scheme: i64, offset: i64, limit: i64) -> Result<Value> {
        permission_versions::versions(self, scheme, offset, limit)
    }
    fn permission_scheme_version(&self, scheme: i64, version: i64) -> Result<Option<Value>> {
        permission_versions::publication(self, scheme, version)
    }
    fn publish_permission_scheme(
        &self,
        scheme: i64,
        body: &Value,
        actor: i64,
        time: &str,
    ) -> Result<i64> {
        permission_versions::publish(self, scheme, body, actor, time)
    }
    fn foundation_candidates(
        &self,
        unit: i64,
        keyword: &str,
        date: &str,
        offset: i64,
        limit: i64,
    ) -> Result<Value> {
        foundation::candidates(self, unit, keyword, date, offset, limit)
    }
    fn foundation_list(
        &self,
        kind: &str,
        unit: i64,
        scope: &DataScope<'_>,
        offset: i64,
        limit: i64,
    ) -> Result<Value> {
        foundation::list(self, kind, unit, scope, offset, limit)
    }
    fn foundation_appointment(&self, unit: i64, key: &str) -> Result<Option<Value>> {
        foundation::appointment(self, unit, key)
    }
    fn foundation_get(&self, kind: &str, unit: i64, id: i64) -> Result<Option<Value>> {
        foundation::get(self, kind, unit, id)
    }
    fn foundation_save(
        &self,
        kind: &str,
        id: Option<i64>,
        unit: i64,
        expected: i64,
        body: &Value,
    ) -> Result<Value> {
        foundation::save(self, kind, id, unit, expected, body)
    }
    fn responsibility_appointments(
        &self,
        unit: i64,
        responsibility: i64,
        project: Option<i64>,
        kind: &str,
        date: &str,
    ) -> Result<Vec<Value>> {
        foundation::resolve_duty(self, unit, responsibility, project, kind, date)
    }
    fn records_by_ids(
        &self,
        kind: &str,
        ids: &[i64],
        fields: &[(&str, &str)],
    ) -> Result<Vec<Value>> {
        let pg = self.provider() == "PostgreSQL";
        let mut result = vec![];
        for ids in ids.chunks(200) {
            operation::check()?;
            let mut values = vec![];
            let kind = sql::bind(&mut values, kind, pg);
            let ids = ids
                .iter()
                .map(|id| format!("CAST({} AS BIGINT)", sql::bind(&mut values, id, pg)))
                .collect::<Vec<_>>()
                .join(",");
            let query = QuerySql {
                filter: format!("r.kind={kind} AND r.id IN ({ids})"),
                order: "r.id DESC".into(),
                values,
            };
            result.extend(self.query_slice(&query, &sql::projection(fields, pg)?, 0, 200)?);
        }
        Ok(result)
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
        let pg = self.provider() == "PostgreSQL";
        let (mut query, projection) = personnel_links::unit_accounts(company, date, all, pg)?;
        if let Some(id) = before {
            let parameter = sql::bind(&mut query.values, id, pg);
            query.filter += &format!(" AND r.id<CAST({parameter} AS BIGINT)");
        }
        self.query_slice(&query, &projection, 0, limit.clamp(1, 200))
    }
    fn resource_has_open_requests(&self, kind: &str, parent: i64) -> Result<bool> {
        let pg = self.provider() == "PostgreSQL";
        Ok(!self
            .query_slice(
                &resource_open_query::build(kind, parent, pg)?,
                &sql::projection(&[("id", "id")], pg)?,
                0,
                1,
            )?
            .is_empty())
    }
    fn query_json(&self, _statement: &str, _values: &[String]) -> Result<Value> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "存储适配器不支持专用聚合查询。",
        ))
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
    fn due_personnel_orders(&self, now: &str) -> Result<Vec<Value>> {
        workflow_schedule::due(self, now)
    }
    fn due_workflow_nodes(&self, now: &str) -> Result<Vec<Value>> {
        workflow_schedule::due_nodes(self, now)
    }
    fn schedule_personnel_order(&self, entry: &Value) -> Result<()> {
        workflow_schedule::schedule(self, entry)
    }
    fn personnel_order_state(&self, id: i64, state: &str, error: &str) -> Result<()> {
        workflow_schedule::state(self, id, state, error)
    }
    fn personnel_order(&self, id: i64) -> Result<Option<Value>> {
        workflow_schedule::order(self, id)
    }
    fn workflow_reminder_level(&self, entry: &Value, level: i64) -> Result<bool> {
        workflow_schedule::reminder(self, entry, level)
    }
    fn command_receipt(&self, actor: i64, context: &str, key: &str) -> Result<Option<Value>> {
        command_receipts::get(self, actor, context, key)
    }
    fn put_command_receipt(&self, receipt: &Value, result: &Value) -> Result<()> {
        command_receipts::put(self, receipt, result)
    }
    fn append_workflow_operation(&self, entry: &Value) -> Result<()> {
        command_receipts::operation(self, entry)
    }
    fn freeze_request_revision(&self, id: i64, snapshot: &Value) -> Result<()> {
        workflow_runtime::freeze(self, id, snapshot)
    }
    fn save_workflow_state(
        &self,
        request: i64,
        revision: i64,
        progress: &WorkflowProgress<'_>,
        plan: &Value,
        active_tasks: &[String],
    ) -> Result<()> {
        workflow_runtime::persist(self, request, revision, progress, plan, active_tasks)
    }
    fn query_audits(&self, _query: &AuditQuery<'_>) -> Result<(i64, Vec<Value>)> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "存储适配器不支持审计筛选分页。",
        ))
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
            keyword: "",
            search_root: None,
            fields: &[("id", "id")],
            offset: 0,
            limit: 1,
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
    fn attachment_category_counts(
        &self,
        _company: &str,
    ) -> Result<std::collections::BTreeMap<i64, i64>> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "存储适配器不支持附件分类统计。",
        ))
    }
    fn import_preview_usage(&self, _owner: i64) -> Result<ImportPreviewUsage> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "存储适配器不支持导入预检容量查询。",
        ))
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
    fn query_hs(&self, query: &HsQuery<'_>) -> Result<Vec<Value>> {
        let (sql, projection) = hs_query::build(query, self.provider() == "PostgreSQL")?;
        self.query_slice(&sql, &projection, query.offset, query.limit)
    }
    fn query_slice(
        &self,
        _sql: &QuerySql,
        _projection: &str,
        _offset: i64,
        _limit: i64,
    ) -> Result<Vec<Value>> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "存储适配器不支持有界扫描。",
        ))
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
    fn job_output_bytes(&self) -> Result<u64> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "存储适配器不支持任务容量查询。",
        ))
    }
    fn query_generic(&self, query: &GenericQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = generic_query::build(query, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    /// Descending keyset scan without a count. The cursor uses the physical
    /// record id even when the caller projects fields that omit the DTO id.
    fn scan_generic(
        &self,
        query: &GenericQuery<'_>,
        before_id: Option<i64>,
    ) -> Result<(Option<i64>, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        let (mut sql, projection) = generic_query::build(query, pg)?;
        if let Some(id) = before_id {
            let cursor = sql::bind(&mut sql.values, id, pg);
            sql.filter
                .push_str(&format!(" AND r.id<CAST({cursor} AS BIGINT)"));
        }
        sql.order = "r.id DESC".into();
        let projection = if pg {
            format!("jsonb_build_array(r.id,({projection})::jsonb)::text")
        } else {
            format!("json_array(r.id,json({projection}))")
        };
        let mut cursor = before_id;
        let mut rows = Vec::new();
        for mut row in self.query_slice(&sql, &projection, 0, query.limit)? {
            let parts = row
                .as_array_mut()
                .filter(|parts| parts.len() == 2)
                .ok_or_else(|| Error::unavailable("记录扫描结果损坏。"))?;
            let id = parts[0]
                .as_i64()
                .filter(|id| *id > 0 && cursor.is_none_or(|cursor| *id < cursor))
                .ok_or_else(|| Error::unavailable("记录扫描游标无效。"))?;
            cursor = Some(id);
            rows.push(parts.pop().expect("validated row projection"));
        }
        Ok((cursor, rows))
    }
    fn resource_references(
        &self,
        kind: &str,
        offset: i64,
        limit: i64,
    ) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = resource_query::build(kind, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, offset, limit)
    }
    fn query_scoped(&self, query: &ScopedQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = generic_query::scoped(query, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    fn query_business(&self, query: &BusinessQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = business_query::build(query, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    fn query_crm(&self, query: &CrmQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = crm_query::build(query, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    fn query_related(&self, query: &RelatedQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = related_query::build(query, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, query.offset, query.limit)
    }
    fn scan_crm(&self, query: &CrmQuery<'_>, after_id: i64) -> Result<Vec<Value>> {
        let (mut sql, projection) = crm_query::build(query, self.provider() == "PostgreSQL")?;
        let cursor = sql::bind(&mut sql.values, after_id, self.provider() == "PostgreSQL");
        sql.filter
            .push_str(&format!(" AND r.id>CAST({cursor} AS BIGINT)"));
        sql.order = "r.id".into();
        self.query_slice(&sql, &projection, 0, query.limit)
    }
    fn query_personnel(&self, query: &PersonnelQuery<'_>) -> Result<(i64, Vec<Value>)> {
        let (sql, projection) = personnel_query::build(query, self.provider() == "PostgreSQL")?;
        self.query_page(&sql, &projection, query.offset, query.limit)
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
        let value = self
            .query_slice(&sql, &projection, 0, 1)?
            .pop()
            .ok_or_else(|| Error::unavailable("模板历史容量统计缺失。"))?;
        TemplateHistoryUsage::from_value(&value)
    }
    fn template_history_candidates(&self, query: &TemplateVersionQuery<'_>) -> Result<Vec<Value>> {
        let (sql, projection) =
            template_history_query::candidates(query, self.provider() == "PostgreSQL");
        self.query_slice(&sql, &projection, query.offset, query.limit)
    }
    fn query_email_template_versions(
        &self,
        query: &TemplateVersionQuery<'_>,
    ) -> Result<(i64, Vec<Value>)> {
        let pg = self.provider() == "PostgreSQL";
        self.query_page(
            &template_query::versions(query, pg),
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
    fn set_settings(&self, name: &str, version: i64, body: &Value) -> Result<()>;
    fn attachment_bytes(&self, invoice_id: i64) -> Result<i64>;
    fn insert_blob(&self, blob: &BlobWrite<'_>) -> Result<()>;
    fn blob(&self, record_id: i64, kind: &str) -> Result<Option<Blob>>;
    fn blob_info(&self, _record_id: i64, _kind: &str) -> Result<Option<BlobInfo>> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "存储适配器不支持文件元数据查询。",
        ))
    }
    fn delete_blobs(&self, record_id: i64) -> Result<()>;
    fn delete_blob(&self, record_id: i64, kind: &str) -> Result<()>;
    fn backup(&self, _path: &Path) -> Result<()> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "该数据库应使用对应的数据库备份模块。",
        ))
    }
    fn verify_backup(&self, _path: &Path) -> Result<()> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "该数据库应使用对应的数据库备份模块。",
        ))
    }
    fn restore(&self, _path: &Path) -> Result<()> {
        Err(Error::new(
            ErrorKind::Unsupported,
            "该数据库应使用对应的数据库恢复模块。",
        ))
    }
}
