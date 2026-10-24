CREATE INDEX records_hs_code_order ON records(kind, (body->>'code') COLLATE "C", id) WHERE kind='hs-codes';
CREATE INDEX records_hs_current_code ON records(kind, lower(body->>'status'), (body->>'effectiveYear'), (body->>'code') COLLATE "C", id) WHERE kind='hs-codes';
CREATE INDEX records_hs_example_order ON records(kind, (CASE WHEN body->>'isManuallyVerified'='true' THEN 0 ELSE 1 END), (body->>'updatedAt') DESC, id) WHERE kind='hs-examples';
CREATE INDEX records_hs_example_rank ON records(kind, (CASE WHEN body->>'isManuallyVerified'='true' THEN 0 ELSE 1 END), (COALESCE((body->>'useCount')::bigint,0)) DESC, (body->>'updatedAt') DESC, id) WHERE kind='hs-examples';
