-- Part of the complete schema 11 baseline; never applied to an existing database.
CREATE TABLE request_revisions (
    id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE RESTRICT,
    request_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    revision INTEGER NOT NULL CHECK(revision>0),
    body TEXT NOT NULL,
    UNIQUE(request_id,revision)
);
CREATE TABLE workflow_instances (
    id TEXT PRIMARY KEY,
    request_id INTEGER NOT NULL,
    revision INTEGER NOT NULL,
    state TEXT NOT NULL CHECK(state IN ('Pending','Returned','Approved','Rejected','Withdrawn','Cancelled','Voided','Completed','HandedOff','Suspended','Terminated','Blocked')),
    generation INTEGER NOT NULL CHECK(generation BETWEEN 1 AND 1000),
    definition TEXT NOT NULL,
    suspended_at TEXT NOT NULL DEFAULT '',
    paused_seconds INTEGER NOT NULL DEFAULT 0 CHECK(paused_seconds>=0),
    FOREIGN KEY(request_id,revision) REFERENCES request_revisions(request_id,revision) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED
);
CREATE UNIQUE INDEX workflow_instances_revision ON workflow_instances(request_id,revision);
CREATE TABLE workflow_subinstances (
 id TEXT PRIMARY KEY,parent_instance_id TEXT NOT NULL REFERENCES workflow_instances(id),
 definition_id INTEGER NOT NULL REFERENCES records(id),definition_version INTEGER NOT NULL,
 submission_id INTEGER NOT NULL REFERENCES request_revisions(id),subflow_key TEXT NOT NULL,
 state TEXT NOT NULL CHECK(state IN('Pending','Approved','Rejected','Returned','Cancelled','Suspended','Blocked')),
 node_keys TEXT NOT NULL,input_snapshot TEXT NOT NULL,
 UNIQUE(parent_instance_id,subflow_key)
);
CREATE TRIGGER subinstance_binding_update BEFORE UPDATE OF parent_instance_id,definition_id,definition_version,submission_id,subflow_key,node_keys,input_snapshot ON workflow_subinstances BEGIN SELECT RAISE(ABORT,'Immutable subflow binding'); END;
CREATE INDEX workflow_instances_state ON workflow_instances(state,request_id,revision);
CREATE TABLE handling_tasks (
    request_id INTEGER NOT NULL,
    revision INTEGER NOT NULL,
    state TEXT NOT NULL CHECK(state IN ('Pending','InProgress','Completed','Blocked','Cancelled')),
    result_status TEXT NOT NULL,
    PRIMARY KEY(request_id,revision),
    FOREIGN KEY(request_id,revision) REFERENCES request_revisions(request_id,revision) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED
);
CREATE TABLE personnel_change_orders (
 request_id INTEGER PRIMARY KEY REFERENCES records(id) ON DELETE RESTRICT,
 revision INTEGER NOT NULL,
 actor_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
 context_id TEXT NOT NULL,
 effective_at TEXT NOT NULL,
 state TEXT NOT NULL CHECK(state IN ('Pending','Applied','Blocked','Cancelled')),
 error_code TEXT NOT NULL,
 FOREIGN KEY(request_id,revision) REFERENCES request_revisions(request_id,revision) ON DELETE RESTRICT
);
CREATE INDEX personnel_orders_due ON personnel_change_orders(state,effective_at,request_id);
CREATE TABLE node_instances (
    instance_id TEXT NOT NULL REFERENCES workflow_instances(id) ON DELETE RESTRICT,
    generation INTEGER NOT NULL CHECK(generation BETWEEN 1 AND 1000),
    node_key TEXT NOT NULL,
    position INTEGER NOT NULL CHECK(position BETWEEN 0 AND 31),
    definition TEXT NOT NULL,
    started_at TEXT NOT NULL,
    due_at TEXT NOT NULL,
    reminder_level INTEGER NOT NULL DEFAULT 0 CHECK(reminder_level BETWEEN 0 AND 2),
    PRIMARY KEY(instance_id,generation,node_key),
    UNIQUE(instance_id,generation,position)
);
CREATE TABLE workflow_tasks (
    request_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    submission INTEGER NOT NULL CHECK(submission>0),
    generation INTEGER NOT NULL CHECK(generation BETWEEN 1 AND 1000),
    task_key TEXT NOT NULL,
    node_key TEXT NOT NULL,
    stage INTEGER NOT NULL CHECK(stage BETWEEN 0 AND 31),
    assignee_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    assignee_name TEXT NOT NULL,
    context_id TEXT NOT NULL,
    acted_by_context TEXT NOT NULL,
    acted_by_id INTEGER REFERENCES records(id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('Waiting','Approved','Rejected','Returned','Skipped','Cancelled')),
    active INTEGER NOT NULL CHECK(active IN (0,1)),
    independent INTEGER NOT NULL CHECK(independent IN (0,1)),
    due_at TEXT NOT NULL,
    started_at TEXT NOT NULL,
    acted_at TEXT NOT NULL,
    elapsed_seconds INTEGER CHECK(elapsed_seconds>=0),
    note TEXT NOT NULL,
    acted_by_name TEXT NOT NULL,
    delegation_key TEXT NOT NULL,
    decision TEXT NOT NULL CHECK(decision IN ('All','Any','Ratio')),
    signature TEXT,
    source_generation INTEGER,
    FOREIGN KEY(request_id,submission,source_generation,task_key) REFERENCES workflow_tasks(request_id,submission,generation,task_key) ON DELETE RESTRICT,
    CHECK(source_generation IS NULL OR (source_generation>0 AND source_generation<generation)),
    CHECK((status IN ('Approved','Rejected','Returned') AND ((signature IS NOT NULL AND source_generation IS NULL) OR (signature IS NULL AND source_generation IS NOT NULL)) AND acted_by_id IS NOT NULL AND started_at<>'' AND acted_at<>'' AND elapsed_seconds IS NOT NULL AND active=0)
        OR (status IN ('Waiting','Skipped','Cancelled') AND signature IS NULL AND source_generation IS NULL AND acted_by_id IS NULL AND elapsed_seconds IS NULL)),
    PRIMARY KEY(request_id,submission,generation,task_key)
);
CREATE INDEX workflow_tasks_inbox ON workflow_tasks(assignee_id,active,status,due_at,request_id);
CREATE INDEX workflow_tasks_actor ON workflow_tasks(acted_by_id,request_id);
CREATE INDEX workflow_tasks_generation ON workflow_tasks(request_id,submission,generation,stage,task_key);
CREATE TABLE workflow_operations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    submission INTEGER NOT NULL,
    generation INTEGER NOT NULL,
    actor_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    context_id TEXT NOT NULL,
    command TEXT NOT NULL,
    reason TEXT NOT NULL,
    occurred_at TEXT NOT NULL,
    details TEXT NOT NULL
);
CREATE TABLE command_receipts (
    actor_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    context_id TEXT NOT NULL,
    command_key TEXT NOT NULL CHECK(length(command_key) BETWEEN 8 AND 128),
    command TEXT NOT NULL,
    target_id INTEGER NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    request_digest TEXT NOT NULL CHECK(length(request_digest)=64),
    result TEXT NOT NULL,
    created_at TEXT NOT NULL,
    PRIMARY KEY(actor_id,context_id,command_key)
);
CREATE VIEW workflow_current_tasks AS SELECT t.* FROM workflow_tasks t JOIN workflow_instances w ON w.request_id=t.request_id AND w.revision=t.submission AND w.generation=t.generation;
CREATE VIEW task_decisions AS SELECT request_id,submission,generation,task_key,assignee_id,context_id,acted_by_id,acted_by_context,status,note,acted_at,delegation_key,signature FROM workflow_tasks WHERE signature IS NOT NULL;
CREATE TRIGGER request_revision_update BEFORE UPDATE ON request_revisions BEGIN SELECT RAISE(ABORT,'Immutable submitted material'); END;
CREATE TRIGGER request_revision_delete BEFORE DELETE ON request_revisions BEGIN SELECT RAISE(ABORT,'Immutable submitted material'); END;
CREATE TRIGGER workflow_signature_update BEFORE UPDATE OF request_id,submission,generation,task_key,node_key,stage,assignee_id,assignee_name,context_id,acted_by_context,acted_by_id,status,independent,due_at,started_at,acted_at,elapsed_seconds,note,acted_by_name,delegation_key,decision,signature,source_generation ON workflow_tasks WHEN OLD.signature IS NOT NULL OR OLD.source_generation IS NOT NULL BEGIN SELECT RAISE(ABORT,'Immutable workflow signature'); END;
CREATE TRIGGER workflow_signature_delete BEFORE DELETE ON workflow_tasks WHEN OLD.signature IS NOT NULL OR OLD.source_generation IS NOT NULL BEGIN SELECT RAISE(ABORT,'Immutable workflow signature'); END;
CREATE TRIGGER workflow_operation_update BEFORE UPDATE ON workflow_operations BEGIN SELECT RAISE(ABORT,'Immutable workflow operation'); END;
CREATE TRIGGER workflow_operation_delete BEFORE DELETE ON workflow_operations BEGIN SELECT RAISE(ABORT,'Immutable workflow operation'); END;
CREATE VIEW workflow_plans AS SELECT w.id,json_set(w.definition,'$.suspendedAt',w.suspended_at,'$.pausedSeconds',w.paused_seconds,'$.generation',w.generation,'$.nodes',json((SELECT json_group_array(json(value)) FROM (SELECT json_set(n.definition,'$.startedAt',n.started_at,'$.dueAt',n.due_at) AS value FROM node_instances n WHERE n.instance_id=w.id AND n.generation=w.generation ORDER BY n.position) q)),'$.steps',json((SELECT json_group_array(json(value)) FROM (SELECT COALESCE(t.signature,json_set(s.signature,'$.sourceGeneration',t.source_generation),json_object('taskKey',t.task_key,'stage',t.stage,'decision',t.decision,'approverUserId',t.assignee_id,'approverName',t.assignee_name,'approverContext',t.context_id,'status',t.status,'actedByUserId',t.acted_by_id,'actedByName',t.acted_by_name,'actedByContext',t.acted_by_context,'actedAt',t.acted_at,'note',t.note,'delegationKey',t.delegation_key,'sourceGeneration',t.source_generation)) AS value FROM workflow_tasks t LEFT JOIN workflow_tasks s ON s.request_id=t.request_id AND s.submission=t.submission AND s.generation=t.source_generation AND s.task_key=t.task_key WHERE t.request_id=w.request_id AND t.submission=w.revision AND t.generation=w.generation ORDER BY t.stage,t.task_key) q))) AS body FROM workflow_instances w;
CREATE VIEW record_rows AS
SELECT r.id,r.kind,r.identity,r.version,r.owner_id,r.company,r.department,r.search_text,
    CASE WHEN w.id IS NULL THEN CASE WHEN v.id IS NULL THEN r.body ELSE (SELECT json_group_object(j.key,json(CASE j.type WHEN 'text' THEN json_quote(j.value) WHEN 'null' THEN 'null' WHEN 'true' THEN 'true' WHEN 'false' THEN 'false' ELSE CAST(j.value AS TEXT) END)) FROM (SELECT a.key,a.value,a.type FROM json_each(v.body) a WHERE NOT EXISTS (SELECT 1 FROM json_each(r.body) b WHERE b.key=a.key) UNION ALL SELECT b.key,b.value,b.type FROM json_each(r.body) b) j) END ELSE json_set(CASE WHEN v.id IS NULL THEN r.body ELSE (SELECT json_group_object(j.key,json(CASE j.type WHEN 'text' THEN json_quote(j.value) WHEN 'null' THEN 'null' WHEN 'true' THEN 'true' WHEN 'false' THEN 'false' ELSE CAST(j.value AS TEXT) END)) FROM (SELECT a.key,a.value,a.type FROM json_each(v.body) a WHERE NOT EXISTS (SELECT 1 FROM json_each(r.body) b WHERE b.key=a.key) UNION ALL SELECT b.key,b.value,b.type FROM json_each(r.body) b) j) END,'$.approvalPlan',json(p.body),'$.status',CASE WHEN w.state='Withdrawn' AND r.kind<>'oa-submission' THEN 'Draft' WHEN w.state='Voided' AND r.kind<>'oa-submission' THEN 'Cancelled' WHEN w.state='Approved' AND r.kind<>'oa-submission' THEN COALESCE(h.result_status,w.state) ELSE w.state END) END AS body
FROM identity_rows r
LEFT JOIN request_revisions v ON v.id=r.id AND r.kind='oa-submission'
LEFT JOIN workflow_instances w ON w.request_id=CASE WHEN v.id IS NOT NULL THEN v.request_id ELSE r.id END
    AND w.revision=CASE WHEN v.id IS NOT NULL THEN v.revision ELSE CAST(NULLIF(r.body->>'submissionNumber','') AS BIGINT) END
LEFT JOIN workflow_plans p ON p.id=w.id
LEFT JOIN handling_tasks h ON h.request_id=w.request_id AND h.revision=w.revision;
