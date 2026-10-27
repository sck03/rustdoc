use export_doc_engine::{api::ApiError, contracts, generated_api::*};
use serde_json::{Value, json};
#[path = "hs_maintenance_contract.rs"]
mod hs_maintenance;

pub fn exercise(
    call: &impl Fn(
        Operation,
        &[(&str, String)],
        &[(&str, String)],
        Option<Value>,
    ) -> Result<Value, ApiError>,
) {
    let reject = |response: Result<Value, ApiError>, expected| {
        assert_eq!(response.unwrap_err().status, Some(expected));
    };
    let run = |op, body| call(op, &[], &[], body);
    let by_id = |op, id: i64| call(op, &[("id", id.to_string())], &[], None);
    let mut plan = json!({"id":0,"expectedVersion":0,"name":"受限装柜回归","containerType":"测试箱",
        "container":{"length":100,"width":100,"height":100,"volume":1,"maxWeight":1000},
        "rules":{"allowRotation":true,"usePalletConstraints":false,"minimumSupportAreaPercent":100,"requireSameFootprintStacking":true},
        "cargoItems":[{"name":"纸箱","length":50,"width":50,"height":50,"weight":10,"quantity":8,"maxTopLoadWeight":10,"preferredZone":"Auto","loadSequence":1,"priorityGroup":"A"}]});
    let analysis = run(ANALYZE_CONTAINER_PACKING, Some(plan.clone())).unwrap();
    assert_eq!(analysis["analysis"]["packedPackages"], 8);
    assert_eq!(analysis["analysis"]["unpackedPackages"], 0);
    export_doc_contracts::validation::response(ANALYZE_CONTAINER_PACKING.id, &analysis).unwrap();
    let projects = run(LIST_CONTAINER_PACKING_PROJECTS, None).unwrap();
    for project in projects["projects"].as_array().unwrap() {
        assert_ne!(project["name"], plan["name"]);
    }
    let saved = run(SAVE_CONTAINER_PACKING_PROJECT, Some(plan.clone())).unwrap();
    let id = saved["id"].as_i64().unwrap();
    plan["id"] = json!(id);
    plan["expectedVersion"] = saved["project"]["versionNumber"].clone();
    let mut updated = plan.clone();
    updated["name"] = json!("装柜方案已复核");
    run(SAVE_CONTAINER_PACKING_PROJECT, Some(updated)).unwrap();
    reject(run(SAVE_CONTAINER_PACKING_PROJECT, Some(plan.clone())), 409);
    let read = by_id(GET_CONTAINER_PACKING_PROJECT, id).unwrap();
    assert_eq!(read["project"]["name"], "装柜方案已复核");
    assert_eq!(read["project"]["cargoItems"], plan["cargoItems"]);
    plan["container"]["maxWeight"] = json!(35);
    let limited = run(ANALYZE_CONTAINER_PACKING, Some(plan.clone())).unwrap();
    assert_eq!(limited["analysis"]["packedPackages"], 3);
    assert_eq!(limited["analysis"]["unpackedPackages"], 5);
    plan["cargoItems"][0]["quantity"] = json!(5001);
    reject(run(ANALYZE_CONTAINER_PACKING, Some(plan)), 400);
    let types = run(LIST_CONTAINER_PACKING_CONTAINER_TYPES, None).unwrap();
    let builtin = types["containerTypes"]
        .as_array()
        .unwrap()
        .iter()
        .find(|t| t["isSystemDefault"] == true)
        .unwrap();
    reject(
        by_id(
            DELETE_CONTAINER_PACKING_CONTAINER_TYPE,
            builtin["id"].as_i64().unwrap(),
        ),
        409,
    );
    by_id(DELETE_CONTAINER_PACKING_PROJECT, id).unwrap();
    reject(by_id(GET_CONTAINER_PACKING_PROJECT, id), 404);

    let mut code = contracts::overlay(
        contracts::object(CREATE_HS_CODE.id, true),
        &json!({"code":"6109.100000","name":"棉制针织T恤衫","status":"ReferenceOnly"}),
    );
    let row = run(CREATE_HS_CODE, Some(code.clone())).unwrap();
    assert_eq!(row["code"], "6109100000");
    export_doc_contracts::validation::response(CREATE_HS_CODE.id, &row).unwrap();
    code["code"] = json!("6109100001");
    code["status"] = json!("Active");
    code["sourceName"] = json!("手工来源");
    code["effectiveYear"] = json!(2026);
    code["lastVerifiedAt"] = json!("2026-09-17T01:00:00Z");
    reject(run(CREATE_HS_CODE, Some(code)), 400);
    let candidate = call(
        SEARCH_HS_CODE_KNOWLEDGE,
        &[],
        &[("query", "610910".into())],
        None,
    )
    .unwrap();
    assert!(
        candidate["items"].as_array().unwrap().is_empty(),
        "unverified references cannot become usable tariffs"
    );
    let feedback = json!({"queryText":"棉制针织T恤衫","productName":"棉制针织T恤衫","specification":"100%棉","candidateCode":"6109100000","accepted":true});
    reject(run(RECORD_HS_CODE_KNOWLEDGE_FEEDBACK, Some(feedback)), 400);
    let update = |row| {
        call(
            UPDATE_HS_CODE,
            &[("code", "6109100000".into())],
            &[],
            Some(row),
        )
    };
    let mut changed = row.clone();
    changed["notes"] = json!("仅供参考");
    update(changed).unwrap();
    reject(update(row), 409);
    hs_maintenance::exercise(call);
}
