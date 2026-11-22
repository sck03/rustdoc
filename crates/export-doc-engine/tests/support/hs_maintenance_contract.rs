use export_doc_engine::{api::ApiError, contracts, generated_api::*};
use serde_json::{Value, json};

pub fn exercise(
    call: &impl Fn(
        Operation,
        &[(&str, String)],
        &[(&str, String)],
        Option<Value>,
    ) -> Result<Value, ApiError>,
) {
    let create = |code| {
        call(
            CREATE_HS_CODE,
            &[],
            &[],
            Some(contracts::overlay(
                contracts::object(CREATE_HS_CODE.id, true),
                &json!({"code":code,"name":"版本保护回归","status":"ReferenceOnly"}),
            )),
        )
        .unwrap()
    };
    let codes = ["9610000000", "9610000001"];
    let a = create(codes[0]);
    let b = create(codes[1]);
    let get = |code: &str| call(GET_HS_CODE, &[("code", code.into())], &[], None).unwrap();
    let version = |row: &Value| json!({"id":row["id"],"rowVersion":row["rowVersion"]});
    let refs = json!([version(&a), version(&b)]);
    let mut edited = b.clone();
    edited["notes"] = json!("他人年度核对后的资料");
    call(
        UPDATE_HS_CODE,
        &[("code", codes[1].into())],
        &[],
        Some(edited),
    )
    .unwrap();
    for (operation, body) in [
        (
            DELETE_HS_CODES_BATCH,
            json!({"ids":[a["id"],b["id"]],"expectedVersions":refs}),
        ),
        (
            CONFIRM_HS_CODES_OBSOLETE,
            json!({"items":refs,"reason":"核对官方公告"}),
        ),
    ] {
        assert_eq!(
            call(operation, &[], &[], Some(body)).unwrap_err().status,
            Some(409)
        );
        assert_eq!(
            get(codes[0])["status"],
            "ReferenceOnly",
            "the entire batch must roll back"
        );
    }
    assert_eq!(
        call(
            DELETE_HS_CODES_BATCH,
            &[],
            &[],
            Some(json!({"ids":[a["id"]]}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let b = get(codes[1]);
    let mut duplicate = b.clone();
    duplicate["id"] = json!(0);
    duplicate["rowVersion"] = json!("native:1");
    duplicate["name"] = json!("过期联网结果");
    duplicate["notes"] = json!("");
    assert_eq!(
        call(CREATE_HS_CODE, &[], &[], Some(duplicate))
            .unwrap_err()
            .status,
        Some(409)
    );
    assert_eq!(
        get(codes[1]),
        b,
        "duplicate creation must preserve the latest local reference"
    );
    let wrong_id = b["id"].as_i64().unwrap() + 100;
    for id in [
        json!(wrong_id),
        json!(wrong_id.to_string()),
        json!(-1),
        json!("-1"),
        json!("invalid"),
        json!(true),
        Value::Null,
    ] {
        let mut wrong = b.clone();
        wrong["id"] = id;
        assert_eq!(
            call(
                UPDATE_HS_CODE,
                &[("code", codes[1].into())],
                &[],
                Some(wrong)
            )
            .unwrap_err()
            .status,
            Some(400)
        );
    }
    let mut stale = b.clone();
    stale["rowVersion"] = json!("native:1");
    assert_eq!(
        call(
            UPDATE_HS_CODE,
            &[("code", codes[1].into())],
            &[],
            Some(stale)
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    assert_eq!(get(codes[1]), b);
    let mut valid = b.clone();
    valid["id"] = json!(b["id"].as_i64().unwrap().to_string());
    let b = call(
        UPDATE_HS_CODE,
        &[("code", codes[1].into())],
        &[],
        Some(valid),
    )
    .unwrap();
    assert_eq!(b["notes"], "他人年度核对后的资料");
    assert_eq!(
        call(
            CONFIRM_HS_CODES_OBSOLETE,
            &[],
            &[],
            Some(json!({"items":[version(&a),version(&b)],"reason":""}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let confirmed = call(
        CONFIRM_HS_CODES_OBSOLETE,
        &[],
        &[],
        Some(json!({"items":[version(&a),version(&b)],"reason":"年度官方公告已确认失效"})),
    )
    .unwrap();
    export_doc_contracts::validation::response(CONFIRM_HS_CODES_OBSOLETE.id, &confirmed).unwrap();
    let a_now = get(codes[0]);
    let b_now = get(codes[1]);
    assert_eq!(a_now["status"], "Obsolete");
    assert_eq!(a_now["obsoleteReason"], "年度官方公告已确认失效");
    assert!(a_now["obsoletedAt"].is_string());
    assert_eq!(
        call(PREVIEW_HS_CODE_CLEANUP, &[], &[], None).unwrap()["eligibleCount"],
        0
    );
    call(
        CONFIRM_HS_CODES_OBSOLETE,
        &[],
        &[],
        Some(json!({"items":[version(&a_now),version(&b_now)],"reason":"重复确认不得重置计时"})),
    )
    .unwrap();
    assert_eq!(get(codes[0]), a_now);
    let mut active = a_now.clone();
    active["status"] = json!("Active");
    assert_eq!(
        call(
            UPDATE_HS_CODE,
            &[("code", codes[0].into())],
            &[],
            Some(active)
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let id = a["id"].as_i64().unwrap().to_string();
    assert_eq!(
        call(
            DELETE_HS_CODE,
            &[("id", id.clone())],
            &[(
                "expectedRowVersion",
                a["rowVersion"].as_str().unwrap().into()
            )],
            None
        )
        .unwrap_err()
        .status,
        Some(409)
    );
    call(
        DELETE_HS_CODE,
        &[("id", id)],
        &[(
            "expectedRowVersion",
            a_now["rowVersion"].as_str().unwrap().into(),
        )],
        None,
    )
    .unwrap();
    call(
        DELETE_HS_CODES_BATCH,
        &[],
        &[],
        Some(json!({"ids":[b_now["id"]],"expectedVersions":[version(&b_now)]})),
    )
    .unwrap();
    assert_eq!(
        call(GET_HS_CODE, &[("code", codes[1].into())], &[], None)
            .unwrap_err()
            .status,
        Some(404)
    );
}
