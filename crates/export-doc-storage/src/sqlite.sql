PRAGMA foreign_keys = ON;
CREATE TABLE schema_version (version INTEGER PRIMARY KEY CHECK(version = 9));
INSERT INTO schema_version VALUES (9);
CREATE TABLE records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    kind TEXT NOT NULL,
    identity TEXT,
    version INTEGER NOT NULL CHECK(version > 0),
    owner_id INTEGER NOT NULL,
    company TEXT NOT NULL,
    department TEXT NOT NULL,
    search_text TEXT NOT NULL,
    body TEXT NOT NULL CHECK(json_valid(body)),
    UNIQUE(kind, identity)
);
CREATE INDEX records_kind_id ON records(kind, id DESC);
CREATE INDEX records_scope ON records(kind, company, department, owner_id);
CREATE INDEX records_company_page ON records(kind, company, id DESC);
CREATE INDEX records_office_employee ON records(kind, company, json_extract(body,'$.employeeId'), json_extract(body,'$.status')) WHERE kind IN ('oa-leave','oa-expense','oa-travel','oa-overtime','oa-purchase','oa-general');
CREATE INDEX records_office_parent ON records(kind, company, json_extract(body,'$.requestId'), id DESC) WHERE kind IN ('oa-event','oa-attachment');
CREATE TABLE history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    kind TEXT NOT NULL,
    record_id INTEGER NOT NULL,
    version INTEGER NOT NULL,
    action TEXT NOT NULL,
    actor_id INTEGER NOT NULL,
    occurred_at TEXT NOT NULL,
    note TEXT NOT NULL,
    body TEXT NOT NULL CHECK(json_valid(body))
);
CREATE INDEX history_record ON history(kind, record_id, id DESC);
CREATE TABLE audit_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    kind TEXT NOT NULL,
    record_id INTEGER NOT NULL,
    version INTEGER NOT NULL,
    action TEXT NOT NULL,
    actor_id INTEGER NOT NULL,
    occurred_at TEXT NOT NULL,
    body TEXT NOT NULL CHECK(json_valid(body))
);
CREATE INDEX audit_logs_time ON audit_logs(occurred_at, id DESC);
CREATE TABLE credentials (
    user_id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE CASCADE,
    salt BLOB NOT NULL,
    password_hash BLOB NOT NULL,
    iterations INTEGER NOT NULL CHECK(iterations >= 600000)
);
CREATE TABLE settings (
    name TEXT PRIMARY KEY,
    version INTEGER NOT NULL,
    body TEXT NOT NULL CHECK(json_valid(body))
);
CREATE TABLE files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    kind TEXT NOT NULL,
    record_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    file_name TEXT NOT NULL,
    media_type TEXT NOT NULL,
    digest TEXT NOT NULL,
    content BLOB NOT NULL,
    created_at TEXT NOT NULL
);
CREATE INDEX files_record ON files(kind, record_id);


CREATE INDEX records_status_page ON records(kind, json_extract(body,'$.status'), id DESC);
CREATE INDEX records_template_catalog ON records(json_extract(body,'$.reportType'), json_extract(body,'$.status'), json_extract(body,'$.name'), id) WHERE kind='report-templates';
CREATE INDEX records_template_version ON records(json_extract(body,'$.templateKind'), json_extract(body,'$.templateId'), json_extract(body,'$.content.versionNumber') DESC, id) WHERE kind='template-versions';
CREATE INDEX records_job_owner_time ON records(owner_id, json_extract(body,'$.createdAt') DESC, id DESC) WHERE kind='background-jobs';


CREATE INDEX records_hs_code_order ON records(kind, json_extract(body,'$.code') COLLATE BINARY, id) WHERE kind='hs-codes';
CREATE INDEX records_hs_current_code ON records(kind, lower(json_extract(body,'$.status')), CAST(json_extract(body,'$.effectiveYear') AS TEXT), json_extract(body,'$.code') COLLATE BINARY, id) WHERE kind='hs-codes';
CREATE INDEX records_hs_example_order ON records(kind, CASE WHEN json_extract(body,'$.isManuallyVerified')=1 THEN 0 ELSE 1 END, json_extract(body,'$.updatedAt') DESC, id) WHERE kind='hs-examples';
CREATE INDEX records_hs_example_rank ON records(kind, CASE WHEN json_extract(body,'$.isManuallyVerified')=1 THEN 0 ELSE 1 END, COALESCE(json_extract(body,'$.useCount'),0) DESC, json_extract(body,'$.updatedAt') DESC, id) WHERE kind='hs-examples';


CREATE TABLE workflow_tasks (
    request_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    submission INTEGER NOT NULL CHECK(submission > 0),
    task_key TEXT NOT NULL,
    node_key TEXT NOT NULL,
    assignee_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    acted_by_id INTEGER,
    status TEXT NOT NULL CHECK(status IN ('Waiting','Approved','Rejected','Returned','Skipped','Cancelled')),
    active INTEGER NOT NULL CHECK(active IN (0,1)),
    due_at TEXT NOT NULL,
    PRIMARY KEY(request_id, submission, task_key)
);
CREATE INDEX workflow_tasks_inbox ON workflow_tasks(assignee_id, active, status, due_at, request_id);
CREATE INDEX workflow_tasks_actor ON workflow_tasks(acted_by_id, request_id);
