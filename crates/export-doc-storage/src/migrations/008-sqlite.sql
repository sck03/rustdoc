CREATE INDEX records_hs_code_order ON records(kind, json_extract(body,'$.code') COLLATE BINARY, id) WHERE kind='hs-codes';
CREATE INDEX records_hs_current_code ON records(kind, lower(json_extract(body,'$.status')), CAST(json_extract(body,'$.effectiveYear') AS TEXT), json_extract(body,'$.code') COLLATE BINARY, id) WHERE kind='hs-codes';
CREATE INDEX records_hs_example_order ON records(kind, CASE WHEN json_extract(body,'$.isManuallyVerified')=1 THEN 0 ELSE 1 END, json_extract(body,'$.updatedAt') DESC, id) WHERE kind='hs-examples';
CREATE INDEX records_hs_example_rank ON records(kind, CASE WHEN json_extract(body,'$.isManuallyVerified')=1 THEN 0 ELSE 1 END, COALESCE(json_extract(body,'$.useCount'),0) DESC, json_extract(body,'$.updatedAt') DESC, id) WHERE kind='hs-examples';
