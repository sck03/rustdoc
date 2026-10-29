PRAGMA foreign_keys = ON;
CREATE TABLE schema_version (version INTEGER PRIMARY KEY CHECK(version = 10));
INSERT INTO schema_version VALUES (10);
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
CREATE TRIGGER organization_units_insert AFTER INSERT ON records WHEN NEW.kind='companies' BEGIN INSERT INTO organization_units(id,code,parent_id,unit_kind) VALUES(NEW.id,NEW.body->>'code',CASE WHEN COALESCE(NEW.body->>'parentCompanyCode','')='' THEN NULL ELSE COALESCE((SELECT id FROM organization_units WHERE code=NEW.body->>'parentCompanyCode'),-1) END,COALESCE(NEW.body->>'unitKind','Company')) ON CONFLICT(id) DO UPDATE SET parent_id=excluded.parent_id,unit_kind=excluded.unit_kind,code=excluded.code; END;
CREATE TRIGGER organization_units_update AFTER UPDATE OF body ON records WHEN NEW.kind='companies' BEGIN INSERT INTO organization_units(id,code,parent_id,unit_kind) VALUES(NEW.id,NEW.body->>'code',CASE WHEN COALESCE(NEW.body->>'parentCompanyCode','')='' THEN NULL ELSE COALESCE((SELECT id FROM organization_units WHERE code=NEW.body->>'parentCompanyCode'),-1) END,COALESCE(NEW.body->>'unitKind','Company')) ON CONFLICT(id) DO UPDATE SET parent_id=excluded.parent_id,unit_kind=excluded.unit_kind,code=excluded.code; END;
CREATE TRIGGER organization_departments_insert AFTER INSERT ON records WHEN NEW.kind='departments' BEGIN INSERT INTO organization_departments(id,unit_id,code,parent_id) VALUES(NEW.id,COALESCE((SELECT id FROM organization_units WHERE code=NEW.body->>'companyCode'),-1),NEW.body->>'code',CASE WHEN COALESCE(NEW.body->>'parentCode','')='' THEN NULL ELSE COALESCE((SELECT d.id FROM organization_departments d JOIN organization_units u ON u.id=d.unit_id WHERE u.code=NEW.body->>'companyCode' AND d.code=NEW.body->>'parentCode'),-1) END) ON CONFLICT(id) DO UPDATE SET code=excluded.code,unit_id=excluded.unit_id,parent_id=excluded.parent_id; END;
CREATE TRIGGER organization_departments_update AFTER UPDATE OF body ON records WHEN NEW.kind='departments' BEGIN INSERT INTO organization_departments(id,unit_id,code,parent_id) VALUES(NEW.id,COALESCE((SELECT id FROM organization_units WHERE code=NEW.body->>'companyCode'),-1),NEW.body->>'code',CASE WHEN COALESCE(NEW.body->>'parentCode','')='' THEN NULL ELSE COALESCE((SELECT d.id FROM organization_departments d JOIN organization_units u ON u.id=d.unit_id WHERE u.code=NEW.body->>'companyCode' AND d.code=NEW.body->>'parentCode'),-1) END) ON CONFLICT(id) DO UPDATE SET code=excluded.code,unit_id=excluded.unit_id,parent_id=excluded.parent_id; END;
CREATE TABLE personnel_memberships (
    person_id INTEGER NOT NULL REFERENCES records(id) ON DELETE CASCADE,
    company TEXT NOT NULL, department TEXT NOT NULL, job_title TEXT NOT NULL,
    starts_on TEXT NOT NULL, ends_on TEXT NOT NULL, is_primary INTEGER NOT NULL CHECK(is_primary IN (0,1)),
    id TEXT PRIMARY KEY NOT NULL CHECK(length(id) BETWEEN 1 AND 64),
    unit_id INTEGER NOT NULL,
    department_id INTEGER NOT NULL,
    FOREIGN KEY(unit_id,company) REFERENCES organization_units(id,code),
    FOREIGN KEY(department_id,unit_id,department) REFERENCES organization_departments(id,unit_id,code),
    UNIQUE(person_id,company,department),
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
CREATE TRIGGER personnel_reporting_insert AFTER INSERT ON personnel_memberships BEGIN
    INSERT INTO reporting_relations SELECT NEW.id,r.body->>'$.organization.managerAssignmentId','Line' FROM records r WHERE r.id=NEW.person_id AND NEW.is_primary=1 AND r.body->>'$.organization.managerAssignmentId' IS NOT NULL;
    INSERT INTO reporting_relations SELECT NEW.id,f.value,'Functional' FROM records r,json_each(COALESCE(r.body->>'$.organization.functionalManagerAssignmentIds','[]')) f WHERE r.id=NEW.person_id AND NEW.is_primary=1;
    INSERT INTO reporting_relations SELECT NEW.id,a.value->>'managerAssignmentId','Line' FROM records r,json_each(COALESCE(r.body->>'$.organization.additionalAssignments','[]')) a WHERE r.id=NEW.person_id AND a.value->>'id'=NEW.id AND a.value->>'managerAssignmentId' IS NOT NULL;
    INSERT INTO reporting_relations SELECT NEW.id,f.value,'Functional' FROM records r,json_each(COALESCE(r.body->>'$.organization.additionalAssignments','[]')) a,json_each(COALESCE(a.value->>'functionalManagerAssignmentIds','[]')) f WHERE r.id=NEW.person_id AND a.value->>'id'=NEW.id;
END;
CREATE TRIGGER personnel_memberships_insert AFTER INSERT ON records WHEN NEW.kind='people' BEGIN
    INSERT INTO personnel_memberships VALUES(NEW.id,NEW.company,NEW.department,COALESCE(NEW.body->>'jobTitle',''),COALESCE(NULLIF(NEW.body->>'$.organization.primaryStartsOn',''),NEW.body->>'hireDate',''),'',1,NEW.body->>'$.organization.primaryAssignmentId',(SELECT id FROM organization_units WHERE code=NEW.company),(SELECT d.id FROM organization_departments d JOIN organization_units u ON u.id=d.unit_id WHERE u.code=NEW.company AND d.code=NEW.department));
    INSERT INTO personnel_memberships SELECT NEW.id,COALESCE(NULLIF(value->>'companyCode',''),NEW.company),value->>'departmentId',COALESCE(value->>'jobTitle',''),COALESCE(value->>'startsOn',''),COALESCE(value->>'endsOn',''),0,value->>'id',(SELECT id FROM organization_units WHERE code=COALESCE(NULLIF(value->>'companyCode',''),NEW.company)),(SELECT d.id FROM organization_departments d JOIN organization_units u ON u.id=d.unit_id WHERE u.code=COALESCE(NULLIF(value->>'companyCode',''),NEW.company) AND d.code=value->>'departmentId') FROM json_each(COALESCE(NEW.body->>'$.organization.additionalAssignments','[]'));
END;
CREATE TRIGGER personnel_memberships_update AFTER UPDATE OF body,company,department ON records WHEN NEW.kind='people' BEGIN
    DELETE FROM personnel_memberships WHERE person_id=NEW.id;
    INSERT INTO personnel_memberships VALUES(NEW.id,NEW.company,NEW.department,COALESCE(NEW.body->>'jobTitle',''),COALESCE(NULLIF(NEW.body->>'$.organization.primaryStartsOn',''),NEW.body->>'hireDate',''),'',1,NEW.body->>'$.organization.primaryAssignmentId',(SELECT id FROM organization_units WHERE code=NEW.company),(SELECT d.id FROM organization_departments d JOIN organization_units u ON u.id=d.unit_id WHERE u.code=NEW.company AND d.code=NEW.department));
    INSERT INTO personnel_memberships SELECT NEW.id,COALESCE(NULLIF(value->>'companyCode',''),NEW.company),value->>'departmentId',COALESCE(value->>'jobTitle',''),COALESCE(value->>'startsOn',''),COALESCE(value->>'endsOn',''),0,value->>'id',(SELECT id FROM organization_units WHERE code=COALESCE(NULLIF(value->>'companyCode',''),NEW.company)),(SELECT d.id FROM organization_departments d JOIN organization_units u ON u.id=d.unit_id WHERE u.code=COALESCE(NULLIF(value->>'companyCode',''),NEW.company) AND d.code=value->>'departmentId') FROM json_each(COALESCE(NEW.body->>'$.organization.additionalAssignments','[]'));
END;
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


CREATE TABLE workflow_tasks (
    request_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    submission INTEGER NOT NULL CHECK(submission > 0),
    task_key TEXT NOT NULL,
    node_key TEXT NOT NULL,
    assignee_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    context_id TEXT NOT NULL,
    acted_by_context TEXT NOT NULL,
    acted_by_id INTEGER,
    status TEXT NOT NULL CHECK(status IN ('Waiting','Approved','Rejected','Returned','Skipped','Cancelled')),
    active INTEGER NOT NULL CHECK(active IN (0,1)),
    independent BIGINT NOT NULL CHECK(independent IN (0,1)),
    due_at TEXT NOT NULL,
    started_at TEXT NOT NULL,
    acted_at TEXT NOT NULL,
    elapsed_seconds BIGINT CHECK(elapsed_seconds >= 0),
    CHECK((status IN ('Approved','Rejected','Returned') AND started_at<>'' AND acted_at<>'' AND elapsed_seconds IS NOT NULL) OR (status IN ('Waiting','Skipped','Cancelled') AND elapsed_seconds IS NULL)),
    PRIMARY KEY(request_id, submission, task_key)
);
CREATE INDEX workflow_tasks_inbox ON workflow_tasks(assignee_id, active, status, due_at, request_id);
CREATE INDEX workflow_tasks_actor ON workflow_tasks(acted_by_id, request_id);

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
CREATE TRIGGER permission_schemes_insert AFTER INSERT ON records WHEN NEW.kind='permission-templates' BEGIN
    INSERT INTO permission_schemes VALUES(NEW.id,NEW.body->>'ownerUnitId',NEW.body->>'code');
END;
CREATE TRIGGER permission_schemes_update AFTER UPDATE OF body ON records WHEN NEW.kind='permission-templates' BEGIN
    UPDATE permission_schemes SET owner_unit_id=NEW.body->>'ownerUnitId',code=NEW.body->>'code' WHERE id=NEW.id;
END;
CREATE TABLE unit_access_grants (
    role_id INTEGER NOT NULL REFERENCES unit_access_roles(id) ON DELETE CASCADE,
    resource_key TEXT NOT NULL,
    action TEXT NOT NULL,
    data_scope TEXT NOT NULL CHECK(data_scope IN ('own','department','company')),
    PRIMARY KEY(role_id,resource_key,action)
);
CREATE TRIGGER personnel_accounts_insert AFTER INSERT ON records WHEN NEW.kind='people' BEGIN
        DELETE FROM personnel_accounts WHERE person_id=NEW.id;
        INSERT INTO personnel_accounts SELECT NEW.id,NEW.body->>'$.account.id' WHERE NEW.body->>'$.account.id' IS NOT NULL;
END;
CREATE TRIGGER personnel_accounts_update AFTER UPDATE OF body ON records WHEN NEW.kind='people' BEGIN
        DELETE FROM personnel_accounts WHERE person_id=NEW.id;
        INSERT INTO personnel_accounts SELECT NEW.id,NEW.body->>'$.account.id' WHERE NEW.body->>'$.account.id' IS NOT NULL;
END;
CREATE TRIGGER organization_duties_insert AFTER INSERT ON records WHEN NEW.kind IN ('companies','departments') BEGIN
        DELETE FROM organization_duties WHERE organization_id=NEW.id;
        INSERT INTO organization_duties SELECT NEW.id,COALESCE(NEW.body->>'executiveAppointmentId',NEW.body->>'managerAppointmentId'),'Primary','[]' WHERE COALESCE(NEW.body->>'executiveAppointmentId',NEW.body->>'managerAppointmentId') IS NOT NULL;
        INSERT INTO organization_duties SELECT NEW.id,a.value->>'appointmentId','Delegate',a.value->>'workflowKinds' FROM json_each(COALESCE(NEW.body->>'additionalLeaders','[]')) a;
END;
CREATE TRIGGER organization_duties_update AFTER UPDATE OF body ON records WHEN NEW.kind IN ('companies','departments') BEGIN
        DELETE FROM organization_duties WHERE organization_id=NEW.id;
        INSERT INTO organization_duties SELECT NEW.id,COALESCE(NEW.body->>'executiveAppointmentId',NEW.body->>'managerAppointmentId'),'Primary','[]' WHERE COALESCE(NEW.body->>'executiveAppointmentId',NEW.body->>'managerAppointmentId') IS NOT NULL;
        INSERT INTO organization_duties SELECT NEW.id,a.value->>'appointmentId','Delegate',a.value->>'workflowKinds' FROM json_each(COALESCE(NEW.body->>'additionalLeaders','[]')) a;
END;
CREATE TRIGGER unit_access_insert AFTER INSERT ON records WHEN NEW.kind='unit-access' BEGIN
        INSERT INTO unit_access_roles VALUES(NEW.id,NEW.body->>'userId',(SELECT id FROM organization_units WHERE code=NEW.body->>'companyCode'),NULLIF(NEW.body->>'appointmentId',''),NEW.body->>'mode',NEW.body->>'isActive',NEW.body->>'permissionTemplateId',NEW.body->>'authorityRoleId') ON CONFLICT(id) DO UPDATE SET user_id=excluded.user_id,unit_id=excluded.unit_id,appointment_id=excluded.appointment_id,mode=excluded.mode,is_active=excluded.is_active,template_id=excluded.template_id,authority_role_id=excluded.authority_role_id;
        DELETE FROM unit_access_grants WHERE role_id=NEW.id;
        INSERT INTO unit_access_grants SELECT NEW.id,g.value->>'resourceKey',g.value->>'action',g.value->>'dataScope' FROM json_each(COALESCE(NEW.body->>'permissionGrants','[]')) g;
END;
CREATE TRIGGER unit_access_update AFTER UPDATE OF body ON records WHEN NEW.kind='unit-access' BEGIN
        INSERT INTO unit_access_roles VALUES(NEW.id,NEW.body->>'userId',(SELECT id FROM organization_units WHERE code=NEW.body->>'companyCode'),NULLIF(NEW.body->>'appointmentId',''),NEW.body->>'mode',NEW.body->>'isActive',NEW.body->>'permissionTemplateId',NEW.body->>'authorityRoleId') ON CONFLICT(id) DO UPDATE SET user_id=excluded.user_id,unit_id=excluded.unit_id,appointment_id=excluded.appointment_id,mode=excluded.mode,is_active=excluded.is_active,template_id=excluded.template_id,authority_role_id=excluded.authority_role_id;
        DELETE FROM unit_access_grants WHERE role_id=NEW.id;
        INSERT INTO unit_access_grants SELECT NEW.id,g.value->>'resourceKey',g.value->>'action',g.value->>'dataScope' FROM json_each(COALESCE(NEW.body->>'permissionGrants','[]')) g;
END;
