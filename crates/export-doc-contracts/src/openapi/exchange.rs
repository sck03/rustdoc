//! Exchange settings and observable refresh state share the existing endpoints.
use serde_json::{Value, json};

#[cfg(test)]
pub(super) const FIELDS: &[(&str, &[&str])] = &[
    (
        "ExchangeRateSettings",
        &["autoRefreshEnabled", "sourceFormat"],
    ),
    (
        "ApiExchangeRateListResponse",
        &[
            "autoRefreshEnabled",
            "sourceFormat",
            "availableCurrencies",
            "missingCurrencies",
            "isStale",
            "lastAttemptAt",
            "nextRefreshAt",
            "lastError",
        ],
    ),
    (
        "ApiExchangeRateAvailableCurrenciesResponse",
        &["isStale", "lastError"],
    ),
];

pub(super) fn extend(doc: &mut Value) {
    let schemas = &mut doc["components"]["schemas"];
    for name in ["ExchangeRateSettings", "ApiExchangeRateListResponse"] {
        schemas[name]["properties"]["autoRefreshEnabled"] =
            json!({"type":"boolean", "default":false});
        schemas[name]["properties"]["sourceFormat"] = json!({
            "type":"string", "enum":["BankOfChinaHtml", "Icbc", "Cmb", "Json", "Auto"], "default":"BankOfChinaHtml",
            "description":"BankOfChinaHtml reads BOC HTML; Icbc and Cmb read the official public quotation endpoints. Json requires baseCurrency CNY, unit 100 and rates using ApiExchangeRateDto."
        });
    }
    for name in [
        "ApiExchangeRateListResponse",
        "ApiExchangeRateAvailableCurrenciesResponse",
    ] {
        schemas[name]["properties"]["isStale"] = json!({"type":"boolean", "default":false});
        schemas[name]["properties"]["lastError"] = json!({"type":"string", "default":""});
    }
    for field in ["availableCurrencies", "missingCurrencies"] {
        schemas["ApiExchangeRateListResponse"]["properties"][field] =
            json!({"type":"array", "items":{"type":"string"}, "default":[]});
    }
    for field in ["lastAttemptAt", "nextRefreshAt"] {
        schemas["ApiExchangeRateListResponse"]["properties"][field] =
            json!({"type":["string","null"], "format":"date-time", "default":null});
    }
    doc["x-exportdoc-configuration"]["defaults"]["exchangeRate"]["autoRefreshEnabled"] =
        json!(false);
    doc["x-exportdoc-configuration"]["defaults"]["exchangeRate"]["sourceFormat"] =
        json!("BankOfChinaHtml");
}
