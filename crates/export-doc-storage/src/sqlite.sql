PRAGMA foreign_keys = ON;
CREATE TABLE schema_model (version INTEGER PRIMARY KEY,model TEXT NOT NULL,shape TEXT NOT NULL);
CREATE TABLE schema_version (version INTEGER PRIMARY KEY CHECK(version = 11));
INSERT INTO schema_version VALUES (11);
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
    UNIQUE(kind, identity),
    CHECK(kind<>'people' OR json_remove(COALESCE(body->>'profile','{}'),'$.fullName','$.workEmail','$.workPhone','$.workLocation')='{}')
);
CREATE TABLE personnel_private_profiles (
    person_id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE CASCADE,
    body TEXT NOT NULL CHECK(json_valid(body) AND json_type(body)='object')
);
CREATE INDEX records_kind_id ON records(kind, id DESC);
CREATE INDEX records_scope ON records(kind, company, department, owner_id);
CREATE INDEX records_company_page ON records(kind, company, id DESC);
CREATE TABLE organization_units (
    id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE CASCADE,
    code TEXT NOT NULL UNIQUE,
    parent_id INTEGER REFERENCES organization_units(id) DEFERRABLE INITIALLY DEFERRED,
    unit_kind TEXT NOT NULL CHECK(unit_kind IN ('Company','Group')),
    UNIQUE(id,code)
);
CREATE INDEX organization_units_parent ON organization_units(parent_id,id);
CREATE TABLE organization_departments (
    id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE CASCADE,
    unit_id INTEGER NOT NULL REFERENCES organization_units(id),
    code TEXT NOT NULL,
    parent_id INTEGER,
    UNIQUE(unit_id,code), UNIQUE(id,unit_id), UNIQUE(id,unit_id,code),
    FOREIGN KEY(parent_id,unit_id) REFERENCES organization_departments(id,unit_id) DEFERRABLE INITIALLY DEFERRED
);
CREATE TABLE personnel_memberships (
    person_id INTEGER NOT NULL REFERENCES records(id) ON DELETE CASCADE,
    company TEXT NOT NULL, department TEXT NOT NULL, job_title TEXT NOT NULL,
    starts_on TEXT NOT NULL, ends_on TEXT NOT NULL, is_primary INTEGER NOT NULL CHECK(is_primary IN (0,1)),
    id TEXT PRIMARY KEY NOT NULL CHECK(length(id) BETWEEN 1 AND 64),
    unit_id INTEGER NOT NULL,
    department_id INTEGER NOT NULL,
    FOREIGN KEY(unit_id,company) REFERENCES organization_units(id,code),
    FOREIGN KEY(department_id,unit_id,department) REFERENCES organization_departments(id,unit_id,code),
    CHECK(starts_on='' OR ends_on='' OR starts_on<=ends_on)
);
CREATE INDEX personnel_memberships_unit ON personnel_memberships(company,department,person_id);
CREATE UNIQUE INDEX personnel_memberships_primary ON personnel_memberships(person_id) WHERE is_primary=1;
CREATE TABLE reporting_relations (
    appointment_id TEXT NOT NULL REFERENCES personnel_memberships(id) ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED,
    manager_appointment_id TEXT NOT NULL REFERENCES personnel_memberships(id) DEFERRABLE INITIALLY DEFERRED,
    relation_type TEXT NOT NULL CHECK(relation_type IN ('Line','Functional')),
    PRIMARY KEY(appointment_id,manager_appointment_id,relation_type),
    CHECK(appointment_id<>manager_appointment_id)
);
CREATE INDEX reporting_relations_manager ON reporting_relations(manager_appointment_id,appointment_id);
CREATE INDEX records_unit_access ON records(json_extract(body,'$.userId'),company,id) WHERE kind='unit-access';
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



-- Relational references are published atomically with the versioned record.
CREATE TABLE personnel_accounts (
    person_id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL UNIQUE REFERENCES records(id),
    UNIQUE(person_id,user_id)
);
CREATE TABLE organization_duties (
    organization_id INTEGER NOT NULL REFERENCES records(id) ON DELETE CASCADE,
    appointment_id TEXT NOT NULL REFERENCES personnel_memberships(id) DEFERRABLE INITIALLY DEFERRED,
    duty_kind TEXT NOT NULL CHECK(duty_kind IN ('Primary','Delegate')),
    workflow_kinds TEXT NOT NULL,
    PRIMARY KEY(organization_id,appointment_id)
);
CREATE INDEX organization_duties_appointment ON organization_duties(appointment_id,organization_id);
CREATE TABLE unit_access_roles (
    id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES records(id),
    unit_id INTEGER NOT NULL REFERENCES organization_units(id),
    appointment_id TEXT REFERENCES personnel_memberships(id) DEFERRABLE INITIALLY DEFERRED,
    mode TEXT NOT NULL CHECK(mode IN ('Business','Collaboration','Administration')),
    source_mode TEXT NOT NULL CHECK(source_mode IN ('Independent','Template')),
    is_active INTEGER NOT NULL CHECK(is_active IN (0,1)),
    template_id INTEGER REFERENCES permission_schemes(id),
    authority_role_id INTEGER REFERENCES unit_access_roles(id) DEFERRABLE INITIALLY DEFERRED,
    CHECK((mode='Business' AND appointment_id IS NOT NULL) OR (mode<>'Business' AND appointment_id IS NULL))
);
CREATE INDEX unit_access_roles_user ON unit_access_roles(user_id,unit_id,is_active);
CREATE TABLE permission_schemes (
    id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE CASCADE,
    owner_unit_id INTEGER REFERENCES organization_units(id),
    code TEXT NOT NULL,
    UNIQUE(owner_unit_id,code)
);
CREATE UNIQUE INDEX permission_schemes_platform_code ON permission_schemes(code) WHERE owner_unit_id IS NULL;
CREATE TABLE unit_access_grants (
    role_id INTEGER NOT NULL REFERENCES unit_access_roles(id) ON DELETE CASCADE,
    resource_key TEXT NOT NULL,
    action TEXT NOT NULL,
    data_scope TEXT NOT NULL CHECK(data_scope IN ('own','department','company')),
    PRIMARY KEY(role_id,resource_key,action)
);
