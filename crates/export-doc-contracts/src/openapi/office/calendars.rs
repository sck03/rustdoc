use super::{object, reference};
use serde_json::{Value, json};
pub(super) fn extend(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    s["OaSubflowReference"] = object(
        json!({"definitionKey":{"type":"string","minLength":1,"maxLength":80},"version":{"type":"integer","minimum":1}}),
        &["definitionKey", "version"],
    );
    s["OaApprovalStage"]["properties"]["subflow"] =
        json!({"anyOf":[reference("OaSubflowReference"),{"type":"null"}]});
    s["OaSubflowInstance"] = object(
        json!({"key":{"type":"string"},"definitionKey":{"type":"string"},"version":{"type":"integer","minimum":1},"definitionId":{"type":"integer","format":"int64","minimum":1},"nodeKeys":{"type":"array","items":{"type":"string"},"maxItems":32}}),
        &[
            "key",
            "definitionKey",
            "version",
            "definitionId",
            "nodeKeys",
        ],
    );
    s["OaApprovalPlan"]["properties"]["subflows"] =
        json!({"type":"array","items":reference("OaSubflowInstance"),"maxItems":32});
    s["OaGraphPoint"] = object(
        json!({"x":{"type":"integer","minimum":0,"maximum":10000},"y":{"type":"integer","minimum":0,"maximum":10000}}),
        &["x", "y"],
    );
    s["OaApprovalStage"]["properties"]["layout"] =
        json!({"anyOf":[reference("OaGraphPoint"),{"type":"null"}]});
    let day = json!({"type":"string","format":"date"});
    s["OaWorkInterval"] = object(
        json!({"startsAt":{"type":"string","pattern":"^[0-2][0-9]:[0-5][0-9]$"},"endsAt":{"type":"string","pattern":"^[0-2][0-9]:[0-5][0-9]$"}}),
        &["startsAt", "endsAt"],
    );
    s["OaBusinessCalendar"] = object(
        json!({"version":{"type":"integer","minimum":1},"timeZone":{"type":"string","minLength":1,"maxLength":100},"workingWeekdays":{"type":"array","items":{"type":"integer","minimum":1,"maximum":7},"minItems":1,"maxItems":7,"uniqueItems":true},"intervals":{"type":"array","items":reference("OaWorkInterval"),"minItems":1,"maxItems":8},"holidays":{"type":"array","items":day,"maxItems":1000,"uniqueItems":true},"additionalWorkingDays":{"type":"array","items":day,"maxItems":1000,"uniqueItems":true}}),
        &[
            "version",
            "timeZone",
            "workingWeekdays",
            "intervals",
            "holidays",
            "additionalWorkingDays",
        ],
    );
    for name in ["OaApprovalSettings", "OaApprovalSettingsSave"] {
        s[name]["properties"]["calendar"] = reference("OaBusinessCalendar");
    }
    let optional = json!({"anyOf":[reference("OaBusinessCalendar"),{"type":"null"}]});
    s["OaApprovalRule"]["properties"]["calendar"] = optional.clone();
    s["OaApprovalNode"]["properties"]["calendarKey"] = json!({"type":"string"});
    s["OaApprovalPlan"]["properties"]["calendars"] =
        json!({"type":"object","additionalProperties":reference("OaBusinessCalendar")});
    for name in ["OaApprovalNode", "OaApprovalStage"] {
        s[name]["properties"]["deadlineMode"] =
            json!({"type":"string","enum":["Natural","Working"],"default":"Natural"});
    }
}
