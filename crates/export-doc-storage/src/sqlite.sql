PRAGMA foreign_keys = ON;
CREATE TABLE schema_version (version INTEGER PRIMARY KEY CHECK(version = 4));
INSERT INTO schema_version VALUES (4);
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
