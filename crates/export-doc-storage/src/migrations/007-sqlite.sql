CREATE INDEX records_status_page ON records(kind, json_extract(body,'$.status'), id DESC);
CREATE INDEX records_template_catalog ON records(json_extract(body,'$.reportType'), json_extract(body,'$.status'), json_extract(body,'$.name'), id) WHERE kind='report-templates';
CREATE INDEX records_template_version ON records(json_extract(body,'$.templateKind'), json_extract(body,'$.templateId'), json_extract(body,'$.content.versionNumber') DESC, id) WHERE kind='template-versions';
CREATE INDEX records_job_owner_time ON records(owner_id, json_extract(body,'$.createdAt') DESC, id DESC) WHERE kind='background-jobs';
