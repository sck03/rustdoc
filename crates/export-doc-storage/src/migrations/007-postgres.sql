CREATE INDEX records_status_page ON records(kind, (body->>'status'), id DESC);
CREATE INDEX records_template_catalog ON records((body->>'reportType'), (body->>'status'), (body->>'name') COLLATE "C", id) WHERE kind='report-templates';
CREATE INDEX records_template_version ON records((body->>'templateKind'), ((body->>'templateId')::bigint), ((body #>> '{content,versionNumber}')::bigint) DESC, id) WHERE kind='template-versions';
CREATE INDEX records_job_owner_time ON records(owner_id, (body->>'createdAt') DESC, id DESC) WHERE kind='background-jobs';

-- Match serde_json's compact, key-sorted representation without duplicating
-- every business document in a persisted search column.
CREATE FUNCTION exportdoc_record_text(value jsonb) RETURNS text
LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $$
  SELECT CASE jsonb_typeof(value)
    WHEN 'object' THEN (SELECT '{' || COALESCE(string_agg(to_jsonb(key)::text || ':' || exportdoc_record_text(item), ',' ORDER BY key COLLATE "C"), '') || '}' FROM jsonb_each(value) AS entries(key,item))
    WHEN 'array' THEN (SELECT '[' || COALESCE(string_agg(exportdoc_record_text(item), ',' ORDER BY ordinal), '') || ']' FROM jsonb_array_elements(value) WITH ORDINALITY AS entries(item,ordinal))
    ELSE value::text END
$$;
GRANT EXECUTE ON FUNCTION exportdoc_record_text(jsonb) TO PUBLIC;
