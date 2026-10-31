CREATE TABLE permission_scheme_versions (
 scheme_id BIGINT NOT NULL REFERENCES permission_schemes(id),version BIGINT NOT NULL CHECK(version>0),
 body JSONB NOT NULL,published_at TEXT NOT NULL,published_by BIGINT REFERENCES records(id),
 PRIMARY KEY(scheme_id,version)
);
CREATE TABLE permission_bindings (
 principal_kind TEXT NOT NULL CHECK(principal_kind IN('Account','Unit')),principal_id BIGINT NOT NULL REFERENCES records(id),
 scheme_id BIGINT NOT NULL,scheme_version BIGINT NOT NULL,reason TEXT NOT NULL,
 PRIMARY KEY(principal_kind,principal_id),FOREIGN KEY(scheme_id,scheme_version) REFERENCES permission_scheme_versions(scheme_id,version)
);
CREATE INDEX permission_bindings_scheme ON permission_bindings(scheme_id,scheme_version,principal_kind,principal_id);
CREATE FUNCTION immutable_permission_publication() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Immutable permission publication' USING ERRCODE='23514'; END $$;
CREATE TRIGGER permission_version_update BEFORE UPDATE OR DELETE ON permission_scheme_versions FOR EACH ROW EXECUTE FUNCTION immutable_permission_publication();
