-- Part of the complete schema 11 baseline; never applied to an existing database.
CREATE TABLE request_revisions (
    id BIGINT PRIMARY KEY REFERENCES records(id) ON DELETE RESTRICT,
    request_id BIGINT NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    revision BIGINT NOT NULL CHECK(revision>0),
    body JSONB NOT NULL,
    UNIQUE(request_id,revision)
);
CREATE TABLE workflow_instances (
    id TEXT PRIMARY KEY,
    request_id BIGINT NOT NULL,
    revision BIGINT NOT NULL,
    state TEXT NOT NULL CHECK(state IN ('Pending','Returned','Approved','Rejected','Withdrawn','Cancelled','Voided','Completed','HandedOff','Suspended','Terminated','Blocked')),
    generation BIGINT NOT NULL CHECK(generation BETWEEN 1 AND 1000),
    definition JSONB NOT NULL,
    suspended_at TEXT NOT NULL DEFAULT '',
    paused_seconds BIGINT NOT NULL DEFAULT 0 CHECK(paused_seconds>=0),
    FOREIGN KEY(request_id,revision) REFERENCES request_revisions(request_id,revision) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED
);
CREATE UNIQUE INDEX workflow_instances_revision ON workflow_instances(request_id,revision);
CREATE TABLE workflow_subinstances (
 id TEXT PRIMARY KEY,parent_instance_id TEXT NOT NULL REFERENCES workflow_instances(id),
 definition_id BIGINT NOT NULL REFERENCES records(id),definition_version BIGINT NOT NULL,
 submission_id BIGINT NOT NULL REFERENCES request_revisions(id),subflow_key TEXT NOT NULL,
 state TEXT NOT NULL CHECK(state IN('Pending','Approved','Rejected','Returned','Cancelled','Suspended','Blocked')),
 node_keys JSONB NOT NULL,input_snapshot JSONB NOT NULL,
 UNIQUE(parent_instance_id,subflow_key)
);
CREATE INDEX workflow_instances_state ON workflow_instances(state,request_id,revision);
CREATE TABLE handling_tasks (
    request_id BIGINT NOT NULL,
    revision BIGINT NOT NULL,
    state TEXT NOT NULL CHECK(state IN ('Pending','InProgress','Completed','Blocked','Cancelled')),
    result_status TEXT NOT NULL,
    PRIMARY KEY(request_id,revision),
    FOREIGN KEY(request_id,revision) REFERENCES request_revisions(request_id,revision) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED
);
CREATE TABLE personnel_change_orders (
 request_id BIGINT PRIMARY KEY REFERENCES records(id) ON DELETE RESTRICT,
 revision BIGINT NOT NULL,
 actor_id BIGINT NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
 context_id TEXT NOT NULL,
 effective_at TEXT NOT NULL,
 state TEXT NOT NULL CHECK(state IN ('Pending','Applied','Blocked','Cancelled')),
 error_code TEXT NOT NULL,
 FOREIGN KEY(request_id,revision) REFERENCES request_revisions(request_id,revision) ON DELETE RESTRICT
);
CREATE INDEX personnel_orders_due ON personnel_change_orders(state,effective_at,request_id);
CREATE TABLE node_instances (
    instance_id TEXT NOT NULL REFERENCES workflow_instances(id) ON DELETE RESTRICT,
    generation BIGINT NOT NULL CHECK(generation BETWEEN 1 AND 1000),
    node_key TEXT NOT NULL,
    position BIGINT NOT NULL CHECK(position BETWEEN 0 AND 31),
    definition JSONB NOT NULL,
    started_at TEXT NOT NULL,
    due_at TEXT NOT NULL,
    reminder_level BIGINT NOT NULL DEFAULT 0 CHECK(reminder_level BETWEEN 0 AND 2),
    PRIMARY KEY(instance_id,generation,node_key),
    UNIQUE(instance_id,generation,position)
);
CREATE TABLE workflow_tasks (
    request_id BIGINT NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    submission BIGINT NOT NULL CHECK(submission>0),
    generation BIGINT NOT NULL CHECK(generation BETWEEN 1 AND 1000),
    task_key TEXT NOT NULL,
    node_key TEXT NOT NULL,
    stage BIGINT NOT NULL CHECK(stage BETWEEN 0 AND 31),
    assignee_id BIGINT NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    assignee_name TEXT NOT NULL,
    context_id TEXT NOT NULL,
    acted_by_context TEXT NOT NULL,
    acted_by_id BIGINT REFERENCES records(id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('Waiting','Approved','Rejected','Returned','Skipped','Cancelled')),
    active BIGINT NOT NULL CHECK(active IN (0,1)),
    independent BIGINT NOT NULL CHECK(independent IN (0,1)),
    due_at TEXT NOT NULL,
    started_at TEXT NOT NULL,
    acted_at TEXT NOT NULL,
    elapsed_seconds BIGINT CHECK(elapsed_seconds>=0),
    note TEXT NOT NULL,
    acted_by_name TEXT NOT NULL,
    delegation_key TEXT NOT NULL,
    decision TEXT NOT NULL CHECK(decision IN ('All','Any','Ratio')),
    signature JSONB,
    source_generation BIGINT,
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
    id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    request_id BIGINT NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    submission BIGINT NOT NULL,
    generation BIGINT NOT NULL,
    actor_id BIGINT NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    context_id TEXT NOT NULL,
    command TEXT NOT NULL,
    reason TEXT NOT NULL,
    occurred_at TEXT NOT NULL,
    details JSONB NOT NULL
);
CREATE TABLE command_receipts (
    actor_id BIGINT NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    context_id TEXT NOT NULL,
    command_key TEXT NOT NULL CHECK(length(command_key) BETWEEN 8 AND 128),
    command TEXT NOT NULL,
    target_id BIGINT NOT NULL REFERENCES records(id) ON DELETE RESTRICT,
    request_digest TEXT NOT NULL CHECK(length(request_digest)=64),
    result JSONB NOT NULL,
    created_at TEXT NOT NULL,
    PRIMARY KEY(actor_id,context_id,command_key)
);
CREATE VIEW workflow_current_tasks AS SELECT t.* FROM workflow_tasks t JOIN workflow_instances w ON w.request_id=t.request_id AND w.revision=t.submission AND w.generation=t.generation;
CREATE VIEW task_decisions AS SELECT request_id,submission,generation,task_key,assignee_id,context_id,acted_by_id,acted_by_context,status,note,acted_at,delegation_key,signature FROM workflow_tasks WHERE signature IS NOT NULL;
CREATE FUNCTION exportdoc_immutable_workflow() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_TABLE_NAME='workflow_tasks' THEN
        IF OLD.signature IS NULL AND OLD.source_generation IS NULL THEN
            IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END IF;
        IF TG_OP='UPDATE' AND (to_jsonb(OLD)-'active')=(to_jsonb(NEW)-'active') THEN RETURN NEW; END IF;
    END IF;
    RAISE EXCEPTION 'Immutable workflow fact' USING ERRCODE='23514';
    IF TG_OP='DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER workflow_signature_immutable BEFORE UPDATE OR DELETE ON workflow_tasks FOR EACH ROW EXECUTE FUNCTION exportdoc_immutable_workflow();
CREATE TRIGGER request_revision_immutable BEFORE UPDATE OR DELETE ON request_revisions FOR EACH ROW EXECUTE FUNCTION exportdoc_immutable_workflow();
CREATE TRIGGER workflow_operation_immutable BEFORE UPDATE OR DELETE ON workflow_operations FOR EACH ROW EXECUTE FUNCTION exportdoc_immutable_workflow();
CREATE VIEW workflow_plans AS SELECT w.id,w.definition || jsonb_build_object('suspendedAt',w.suspended_at,'pausedSeconds',w.paused_seconds,'generation',w.generation,'nodes',COALESCE((SELECT jsonb_agg(value) FROM (SELECT n.definition || jsonb_build_object('startedAt',n.started_at,'dueAt',n.due_at) AS value FROM node_instances n WHERE n.instance_id=w.id AND n.generation=w.generation ORDER BY n.position) q),'[]'::jsonb),'steps',COALESCE((SELECT jsonb_agg(value) FROM (SELECT COALESCE(t.signature,(s.signature || jsonb_build_object('sourceGeneration',t.source_generation)),jsonb_build_object('taskKey',t.task_key,'stage',t.stage,'decision',t.decision,'approverUserId',t.assignee_id,'approverName',t.assignee_name,'approverContext',t.context_id,'status',t.status,'actedByUserId',t.acted_by_id,'actedByName',t.acted_by_name,'actedByContext',t.acted_by_context,'actedAt',t.acted_at,'note',t.note,'delegationKey',t.delegation_key,'sourceGeneration',t.source_generation)) AS value FROM workflow_tasks t LEFT JOIN workflow_tasks s ON s.request_id=t.request_id AND s.submission=t.submission AND s.generation=t.source_generation AND s.task_key=t.task_key WHERE t.request_id=w.request_id AND t.submission=w.revision AND t.generation=w.generation ORDER BY t.stage,t.task_key) q),'[]'::jsonb)) AS body FROM workflow_instances w;
CREATE VIEW record_rows AS
SELECT r.id,r.kind,r.identity,r.version,r.owner_id,r.company,r.department,r.search_text,
    CASE WHEN w.id IS NULL THEN COALESCE(v.body,'{}'::jsonb) || r.body ELSE COALESCE(v.body,'{}'::jsonb) || r.body || jsonb_build_object('approvalPlan',p.body,'status',CASE WHEN w.state='Withdrawn' AND r.kind<>'oa-submission' THEN 'Draft' WHEN w.state='Voided' AND r.kind<>'oa-submission' THEN 'Cancelled' WHEN w.state='Approved' AND r.kind<>'oa-submission' THEN COALESCE(h.result_status,w.state) ELSE w.state END) END AS body
FROM identity_rows r
LEFT JOIN request_revisions v ON v.id=r.id AND r.kind='oa-submission'
LEFT JOIN workflow_instances w ON w.request_id=CASE WHEN v.id IS NOT NULL THEN v.request_id ELSE r.id END
    AND w.revision=CASE WHEN v.id IS NOT NULL THEN v.revision ELSE CAST(NULLIF(r.body->>'submissionNumber','') AS BIGINT) END
LEFT JOIN workflow_plans p ON p.id=w.id
LEFT JOIN handling_tasks h ON h.request_id=w.request_id AND h.revision=w.revision;
