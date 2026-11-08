use export_doc_engine::{api::ApiError, generated_api::*};
use serde_json::{Value, json};
pub fn exercise(
    call: &impl Fn(
        Operation,
        &[(&str, String)],
        &[(&str, String)],
        Option<Value>,
    ) -> Result<Value, ApiError>,
) {
    let run = |op, body| call(op, &[], &[], Some(body));
    let conflict =
        |result: Result<Value, ApiError>| assert_eq!(result.unwrap_err().status, Some(409));
    let catalog = call(GET_SINGLE_WINDOW_REFERENCE_CATALOG, &[], &[], None).unwrap();
    let request = json!({"catalog":catalog["catalog"],"expectedRevision":catalog["revision"]});
    let saved = run(UPDATE_SINGLE_WINDOW_REFERENCE_CATALOG, request.clone()).unwrap();
    conflict(run(UPDATE_SINGLE_WINDOW_REFERENCE_CATALOG, request));
    conflict(call(
        RESET_SINGLE_WINDOW_REFERENCE_CATALOG,
        &[],
        &[("expectedRevision", catalog["revision"].to_string())],
        None,
    ));
    let reset = call(
        RESET_SINGLE_WINDOW_REFERENCE_CATALOG,
        &[],
        &[("expectedRevision", saved["revision"].to_string())],
        None,
    )
    .unwrap();
    assert_eq!(
        reset["revision"].as_i64().unwrap(),
        saved["revision"].as_i64().unwrap() + 1
    );

    let profile = json!({"ciqRegNo":"VERS-TEST","prdcEtpsName":"版本企业","producer":"before"});
    let saved = run(
        CREATE_CUSTOMS_COO_PRODUCER_PROFILE,
        json!({"profile":profile}),
    )
    .unwrap();
    let id = saved["id"].to_string();
    let path = [("id", id)];
    let request = json!({"profile":profile,"expectedVersion":saved["profile"]["versionNumber"]});
    let mut request = request;
    request["profile"]["producer"] = json!("after");
    let next = call(
        UPDATE_CUSTOMS_COO_PRODUCER_PROFILE,
        &path,
        &[],
        Some(request.clone()),
    )
    .unwrap();
    conflict(call(
        UPDATE_CUSTOMS_COO_PRODUCER_PROFILE,
        &path,
        &[],
        Some(request),
    ));
    conflict(call(
        UPDATE_CUSTOMS_COO_PRODUCER_PROFILE,
        &path,
        &[],
        Some(json!({"profile":profile})),
    ));
    conflict(call(
        DELETE_CUSTOMS_COO_PRODUCER_PROFILE,
        &path,
        &[(
            "expectedVersion",
            saved["profile"]["versionNumber"].to_string(),
        )],
        None,
    ));
    let list = call(
        LIST_CUSTOMS_COO_PRODUCER_PROFILES,
        &[],
        &[
            ("keyword", "版本企业".into()),
            ("pageSize", "1".into()),
            ("pageNumber", "1".into()),
        ],
        None,
    )
    .unwrap();
    assert_eq!(list["items"][0]["producer"], "after");
    assert_eq!(list["totalCount"], 1);
    call(
        DELETE_CUSTOMS_COO_PRODUCER_PROFILE,
        &path,
        &[(
            "expectedVersion",
            next["profile"]["versionNumber"].to_string(),
        )],
        None,
    )
    .unwrap();

    let request = json!({"name":"版本柜型","length":100,"width":100,"height":100,"maxVolume":1,"maxWeight":100});
    let saved = run(SAVE_CONTAINER_PACKING_CONTAINER_TYPE, request.clone()).unwrap();
    conflict(run(SAVE_CONTAINER_PACKING_CONTAINER_TYPE, request.clone()));
    let mut update = request;
    update["id"] = saved["id"].clone();
    conflict(run(SAVE_CONTAINER_PACKING_CONTAINER_TYPE, update.clone()));
    update["expectedVersion"] = saved["containerType"]["versionNumber"].clone();
    update["length"] = json!(110);
    let next = run(SAVE_CONTAINER_PACKING_CONTAINER_TYPE, update.clone()).unwrap();
    conflict(run(SAVE_CONTAINER_PACKING_CONTAINER_TYPE, update));
    let path = [("id", saved["id"].to_string())];
    conflict(call(
        DELETE_CONTAINER_PACKING_CONTAINER_TYPE,
        &path,
        &[(
            "expectedVersion",
            saved["containerType"]["versionNumber"].to_string(),
        )],
        None,
    ));
    call(
        DELETE_CONTAINER_PACKING_CONTAINER_TYPE,
        &path,
        &[(
            "expectedVersion",
            next["containerType"]["versionNumber"].to_string(),
        )],
        None,
    )
    .unwrap();
}
