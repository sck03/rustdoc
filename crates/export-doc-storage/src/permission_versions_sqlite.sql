CREATE TABLE permission_scheme_versions (
 scheme_id INTEGER NOT NULL REFERENCES permission_schemes(id),version INTEGER NOT NULL CHECK(version>0),
 body TEXT NOT NULL,published_at TEXT NOT NULL,published_by INTEGER REFERENCES records(id),
 PRIMARY KEY(scheme_id,version)
);
CREATE TABLE permission_bindings (
 principal_kind TEXT NOT NULL CHECK(principal_kind IN('Account','Unit')),principal_id INTEGER NOT NULL REFERENCES records(id),
 scheme_id INTEGER NOT NULL,scheme_version INTEGER NOT NULL,reason TEXT NOT NULL,
 PRIMARY KEY(principal_kind,principal_id),FOREIGN KEY(scheme_id,scheme_version) REFERENCES permission_scheme_versions(scheme_id,version)
);
CREATE INDEX permission_bindings_scheme ON permission_bindings(scheme_id,scheme_version,principal_kind,principal_id);
CREATE TRIGGER permission_version_update BEFORE UPDATE ON permission_scheme_versions BEGIN SELECT RAISE(ABORT,'Immutable permission publication'); END;
CREATE TRIGGER permission_version_delete BEFORE DELETE ON permission_scheme_versions BEGIN SELECT RAISE(ABORT,'Immutable permission publication'); END;
