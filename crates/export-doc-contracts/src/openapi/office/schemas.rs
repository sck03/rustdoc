use super::{object, reference};
use serde_json::{Value, json};

pub(super) fn extend(doc: &mut Value) {
    let string = json!({"type":"string"});
    let integer = json!({"type":"integer","format":"int64"});
    let money = json!({"type":"string","pattern":"^[0-9]+(\\.[0-9]{1,2})?$"});
    let date = json!({"type":"string","format":"date"});
    let schemas = &mut doc["components"]["schemas"];
    schemas["PersonnelClearance"]["properties"]["approvalCount"] = integer.clone();
    schemas["OaLeave"] = object(
        json!({
            "category":{"type":"string","enum":["Annual","Sick","Personal","Other"]},
            "startsOn":date,"endsOn":date,
            "startPeriod":{"type":"string","enum":["AM","PM"]},"endPeriod":{"type":"string","enum":["AM","PM"]}
        }),
        &["category", "startsOn", "endsOn", "startPeriod", "endPeriod"],
    );
    schemas["OaExpenseLine"] = object(
        json!({
            "category":{"type":"string","enum":["Travel","Transport","Meals","Office","Other"]},
            "spentOn":date,"description":string,"amount":money
        }),
        &["category", "spentOn", "description", "amount"],
    );
    schemas["OaPurchaseLine"] = object(
        json!({"name":string,"specification":string,"quantity":{"type":"string","pattern":"^[0-9]+(\\.[0-9]{1,3})?$"},"unit":string,"unitPrice":money}),
        &["name", "quantity", "unit", "unitPrice"],
    );
    schemas["OaTravel"] = object(
        json!({"destination":string,"startsOn":date,"endsOn":date}),
        &["destination", "startsOn", "endsOn"],
    );
    schemas["OaOvertime"] = object(
        json!({"startsAt":{"type":"string","format":"date-time"},"endsAt":{"type":"string","format":"date-time"},"location":string}),
        &["startsAt", "endsAt", "location"],
    );
    schemas["OaRequestSave"] = object(
        json!({
            "requestKey":string,"expectedVersion":integer,"employeeId":integer,"title":string,"reason":string,
            "leave":reference("OaLeave"),"travel":reference("OaTravel"),"overtime":reference("OaOvertime"),
            "category":{"type":"string","enum":["Seal","Certificate","IT","Repair","Other"]},
            "currency":{"type":"string","enum":["CNY","USD","EUR","HKD","JPY","GBP"]},
            "lines":{"type":"array","items":reference("OaExpenseLine"),"maxItems":100},
            "purchaseLines":{"type":"array","items":reference("OaPurchaseLine"),"maxItems":100}
        }),
        &["requestKey", "title", "reason"],
    );
    schemas["OaAction"] = object(
        json!({"expectedVersion":integer,"note":string}),
        &["expectedVersion", "note"],
    );
    schemas["OaAttachment"] = object(
        json!({"id":integer,"fileName":string,"mediaType":string,"sizeBytes":integer}),
        &["id", "fileName", "mediaType", "sizeBytes"],
    );
    schemas["OaEvent"] = object(
        json!({"id":integer,"action":string,"actorName":string,"occurredAt":string,"note":string,"requestVersion":integer}),
        &[
            "id",
            "action",
            "actorName",
            "occurredAt",
            "note",
            "requestVersion",
        ],
    );
    let mut record = schemas["OaRequestSave"]["properties"].clone();
    for (key,value) in json!({"id":integer,"versionNumber":integer,"employeeName":string,"departmentId":string,"ownerUserId":integer,
        "status":{"type":"string","enum":["Draft","Pending","Approved","Rejected","Cancelled","Completed","HandedOff"]},
        "kind":{"type":"string","enum":["leave","expense","travel","overtime","purchase","general"]},"totalAmount":string,"durationDays":string,"durationHours":string,
        "createdAt":string,"updatedAt":string,"attachments":{"type":"array","items":reference("OaAttachment")}
    }).as_object().unwrap() { record[key] = value.clone(); }
    schemas["OaRequest"] = object(
        record,
        &[
            "id",
            "versionNumber",
            "requestKey",
            "title",
            "reason",
            "employeeId",
            "employeeName",
            "departmentId",
            "ownerUserId",
            "kind",
            "status",
            "totalAmount",
            "durationDays",
            "durationHours",
            "createdAt",
            "updatedAt",
            "attachments",
        ],
    );
    for (name, item) in [("OaRequestPage", "OaRequest"), ("OaEventPage", "OaEvent")] {
        schemas[name] = object(
            json!({"items":{"type":"array","items":reference(item)},"totalCount":integer,"pageNumber":integer,"pageSize":integer}),
            &["items", "totalCount", "pageNumber", "pageSize"],
        );
    }
}
