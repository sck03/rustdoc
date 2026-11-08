//! Explicit reviewed changes to the frozen served contract; never copy live output.
use serde_json::{Value, json};
pub(super) fn reviewed_versioning(baseline: &mut Value) {
    for name in ["ApiCustomsCooProducerProfileDto", "ApiContainerTypeDto"] {
        let schema = &mut baseline["components"]["schemas"][name];
        schema["properties"]["versionNumber"] =
            json!({"type":"integer","minimum":1,"format":"int64"});
        schema["required"]
            .as_array_mut()
            .unwrap()
            .insert(1, json!("versionNumber"));
    }
    for name in [
        "ApiCustomsCooProducerProfileSaveRequest",
        "ApiContainerTypeSaveRequest",
    ] {
        baseline["components"]["schemas"][name]["properties"]["expectedVersion"] =
            json!({"type":"integer","minimum":1,"format":"int64"});
    }
    for (name, field, minimum, index) in [
        ("ApiSingleWindowReferenceCatalogResponse", "revision", 0, 1),
        (
            "ApiSingleWindowReferenceCatalogSaveRequest",
            "expectedRevision",
            0,
            1,
        ),
        (
            "ApiSingleWindowReferenceCatalogSaveResponse",
            "revision",
            1,
            2,
        ),
    ] {
        let schema = &mut baseline["components"]["schemas"][name];
        schema["properties"][field] = json!({"type":"integer","minimum":minimum,"format":"int64"});
        schema["required"]
            .as_array_mut()
            .unwrap()
            .insert(index, json!(field));
    }
    for path in [
        "/api/single-window/coo/producer-profiles/{id}",
        "/api/tools/container-packing/projects/{id}",
        "/api/tools/container-packing/container-types/{id}",
    ] {
        baseline["paths"][path]["delete"]["parameters"].as_array_mut().unwrap().push(json!({"name":"expectedVersion","in":"query","required":true,"schema":{"type":"integer","minimum":1,"format":"int64"}}));
    }
    let conflict = baseline["paths"]["/api/tools/container-packing/container-types/{id}"]["delete"]
        ["responses"]["409"]
        .clone();
    baseline["paths"]["/api/tools/container-packing/projects/{id}"]["delete"]["responses"]["409"] =
        conflict;
    for (path, method) in [
        ("/api/single-window/reference-catalog", "delete"),
        ("/api/single-window/reference-catalog/import-json", "post"),
    ] {
        baseline["paths"][path][method]["parameters"] = json!([{"name":"expectedRevision","in":"query","required":true,"schema":{"type":"integer","minimum":0,"format":"int64"}}]);
    }
    let parameters =
        baseline["paths"]["/api/single-window/coo/producer-profiles"]["get"]["parameters"]
            .as_array_mut()
            .unwrap();
    for name in ["pageNumber", "pageSize"] {
        parameters.push(json!({"name":name,"in":"query","schema":{"type":["integer","string"],"format":"int32","pattern":"^-?(?:0|[1-9]\\d*)$"}}));
    }
}
