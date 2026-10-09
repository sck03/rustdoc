use export_doc_engine::{api::ApiError, generated_api::*, invoice::InvoiceDraft, paths::nonce};
use serde_json::{Value, json};

pub fn exercise(
    request: &dyn Fn(
        Operation,
        &[(&str, String)],
        &[(&str, String)],
        Option<Value>,
    ) -> Result<Value, ApiError>,
) {
    let number = format!("CLONE-{}", nonce().unwrap());
    let call = |op: Operation, id: i64, body: Value| {
        let path = if id > 0 {
            vec![("id", id.to_string())]
        } else {
            vec![]
        };
        let result = request(op, &path, &[], Some(body)).unwrap();
        export_doc_contracts::validation::response(op.id, &result).unwrap();
        result
    };
    let draft = InvoiceDraft::demo("2001-02-03", &number).build().unwrap();
    assert_eq!(draft.r#type, "报关数据");
    assert_eq!(draft.supervision_mode, "一般贸易");
    assert_eq!(draft.transport_mode, "BY SEA");
    let source = call(CREATE_INVOICE, 0, json!(draft));
    let id = source["id"].as_i64().unwrap();
    let path = [("id", id.to_string())];
    let body = json!({"targetType":"实际数据","options":{"copyHeader":true,"copyItems":true,"resetDates":false,"clearAmounts":false}});
    let actual = call(CLONE_INVOICE_AS_TYPE, id, body.clone());
    let actual_id = actual["id"].as_i64().unwrap();
    assert_ne!(actual_id, id);
    assert_eq!(actual["invoice"]["id"], actual_id);
    for field in [
        "invoiceNo",
        "invoiceDate",
        "shipmentDate",
        "ownerUserId",
        "departmentId",
        "companyScope",
        "totalAmount",
    ] {
        assert_eq!(
            actual["invoice"][field], source["invoice"][field],
            "{field}"
        );
    }
    assert_eq!(actual["invoice"]["type"], "实际数据");
    assert_eq!(actual["invoice"]["status"], "Draft");
    assert!(
        actual["invoice"]["items"]
            .as_array()
            .unwrap()
            .iter()
            .all(|item| item["invoiceId"] == actual_id)
    );
    assert_eq!(
        request(CLONE_INVOICE_AS_TYPE, &path, &[], Some(body))
            .unwrap_err()
            .status,
        Some(409)
    );
    for target in ["报关数据", "", "invalid"] {
        assert_eq!(
            request(
                CLONE_INVOICE_AS_TYPE,
                &path,
                &[],
                Some(json!({"targetType":target,"options":{}}))
            )
            .unwrap_err()
            .status,
            Some(400)
        );
    }
    assert_eq!(
        request(
            CLONE_INVOICE,
            &path,
            &[],
            Some(json!({"newInvoiceNo":"","options":{}}))
        )
        .unwrap_err()
        .status,
        Some(400)
    );
    let list = request(LIST_INVOICES, &[], &[("keyword", number.clone())], None).unwrap();
    assert_eq!(
        list["totalCount"], 2,
        "failed or repeated requests must not create records"
    );
    let copy = call(
        CLONE_INVOICE,
        id,
        json!({"newInvoiceNo":format!("{number}-NEW"),"options":{"resetDates":true,"clearAmounts":true}}),
    );
    assert_eq!(copy["invoice"]["invoiceNo"], format!("{number}-NEW"));
    assert_ne!(copy["invoice"]["invoiceDate"], "2001-02-03");
    assert_eq!(copy["invoice"]["totalAmount"], 0);
    assert_eq!(
        copy["invoice"]["totalQuantity"],
        source["invoice"]["totalQuantity"]
    );
    assert!(copy["invoice"]["items"].as_array().unwrap().iter().all(|item| item["unitPrice"] == 0 && item["priceCalculationMode"] == "UnitPriceDriven"));
    let empty = call(
        CLONE_INVOICE,
        id,
        json!({"newInvoiceNo":format!("{number}-EMPTY"),"options":{"copyHeader":false,"copyItems":false,"resetDates":false}}),
    );
    assert_eq!(empty["invoice"]["customerNameEN"], "");
    assert_eq!(empty["invoice"]["items"], json!([]));
    assert_eq!(empty["invoice"]["totalAmount"], 0);
    let reloaded = request(GET_INVOICE, &path, &[], None).unwrap();
    assert_eq!(
        reloaded, source["invoice"],
        "cloning must preserve its source"
    );
}
