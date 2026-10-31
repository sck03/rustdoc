CREATE TABLE access_restrictions (
 id INTEGER PRIMARY KEY AUTOINCREMENT,principal_kind TEXT NOT NULL CHECK(principal_kind IN('Account','Unit')),
 principal_id INTEGER NOT NULL REFERENCES records(id),resource_key TEXT NOT NULL,action TEXT NOT NULL,
 mode TEXT NOT NULL CHECK(mode IN('Deny','Unrestricted','Units','Departments','Projects','Participation')),
 reason TEXT NOT NULL,version INTEGER NOT NULL CHECK(version>0),
 UNIQUE(principal_kind,principal_id,resource_key,action)
);
CREATE TABLE access_scope_members (
 restriction_id INTEGER NOT NULL REFERENCES access_restrictions(id) ON DELETE CASCADE,
 unit_id INTEGER REFERENCES organization_units(id),department_id INTEGER REFERENCES organization_departments(id),project_id INTEGER REFERENCES project_scopes(id),
 CHECK((unit_id IS NOT NULL AND department_id IS NULL AND project_id IS NULL) OR (unit_id IS NULL AND department_id IS NOT NULL AND project_id IS NULL) OR (unit_id IS NULL AND department_id IS NULL AND project_id IS NOT NULL))
);
CREATE INDEX access_restrictions_principal ON access_restrictions(principal_kind,principal_id,resource_key,action);
