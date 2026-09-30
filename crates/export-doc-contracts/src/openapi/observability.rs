use serde_json::{Value, json};
fn object(properties: Value) -> Value {
    let required: Vec<_> = properties.as_object().unwrap().keys().cloned().collect();
    json!({"type":"object","properties":properties,"required":required,"additionalProperties":false})
}
fn counter() -> Value {
    json!({"type":"integer","format":"int64","minimum":0})
}
fn reference(name: &str) -> Value {
    json!({"$ref":format!("#/components/schemas/{name}")})
}
pub(super) fn extend(document: &mut Value) {
    let schemas = &mut document["components"]["schemas"];
    schemas["ApiRuntimeMetricBucket"] = object(
        json!({"upperBoundMicroseconds":{"type":["integer","null"],"format":"int64"},"count":counter()}),
    );
    schemas["ApiRuntimeLatencyMetrics"] = object(
        json!({"completed":counter(),"failures":counter(),"active":counter(),"totalMicroseconds":counter(),"maxMicroseconds":counter(),"p95UpperBoundMicroseconds":counter(),"p99UpperBoundMicroseconds":counter(),"buckets":{"type":"array","items":reference("ApiRuntimeMetricBucket")}}),
    );
    let latency = reference("ApiRuntimeLatencyMetrics");
    schemas["ApiRuntimeDatabaseMetrics"] = object(
        json!({"capacity":counter(),"leased":counter(),"failed":{"type":"boolean"},"acquireTimeouts":counter(),"acquireTimeoutMilliseconds":counter(),"wait":latency,"operations":latency,"writeWait":latency,"writeTransactions":latency,"writeCoordinatorFailed":{"type":"boolean"}}),
    );
    schemas["ApiRuntimeJobMetrics"] = object(
        json!({"active":counter(),"workerHandles":counter(),"stopping":{"type":"boolean"},"failed":{"type":"boolean"},"persistedCounts":{"type":"object","additionalProperties":counter()}}),
    );
    schemas["ApiRuntimeHttpMetrics"] = object(
        json!({"requests":latency,"queueWait":latency,"queueCapacity":counter(),"queuedRequests":counter(),"admissionRejected":counter(),"bulkRejected":counter(),"requestCapacity":counter(),"availableRequestSlots":counter(),"bulkCapacity":counter(),"availableBulkSlots":counter(),"uptimeSeconds":counter(),"logsDropped":counter(),"logWriteErrors":counter()}),
    );
    schemas["ApiRuntimeMetricsResponse"] = object(
        json!({"checkedAt":{"type":"string","format":"date-time"},"storage":reference("ApiRuntimeDatabaseMetrics"),"jobs":reference("ApiRuntimeJobMetrics"),"http":{"anyOf":[reference("ApiRuntimeHttpMetrics"),{"type":"null"}]}}),
    );
    document["paths"]["/api/diagnostics/metrics"] = json!({"get":{
        "tags":["ExportDocManager.Api"],"operationId":"GetRuntimeMetrics",
        "responses":{"200":{"description":"Process counters and retained task counts; no business data.","content":{"application/json":{"schema":reference("ApiRuntimeMetricsResponse")}}},"401":{"description":"Authentication required"},"403":{"description":"Administrator required"},"503":{"description":"Dependency unavailable"}},
        "security":[{"BearerAuth":[],"DesktopAccess":[]}],
        "x-exportdoc-policy":{"requiresAuthentication":true,"requiresDesktopAccess":true,"requiresLicense":false,"permissions":null,"requirements":[{"resourceKey":"system.settings","action":"view"}],"permissionBypass":false}
    }});
}
