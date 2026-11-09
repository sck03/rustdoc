use export_doc_contracts::contracts;
use export_doc_domain::single_window::{Business, catalog};
use serde_json::{Value, json};

pub fn acd() -> Value {
    let mut doc = Business::Acd.blank();
    for (key, value) in [
        ("copCusCode", "TEST000001"),
        ("gName", "测试棉制针织衫"),
        ("codeTS", "6109100010"),
        ("declTotal", "100"),
        ("ieDate", "20261109"),
        ("tradeMode", "0110"),
        ("oriCountry", "142"),
        ("tradeCode", "1234567890"),
        ("agentCode", "0987654321"),
    ] {
        doc[key] = json!(value);
    }
    doc
}

pub fn coo(cert: &str, country: &str, sequence: &str) -> Value {
    let mut doc = Business::Coo.blank();
    for (key, value) in [
        ("applyType", "0"),
        ("certStatus", "0"),
        ("certType", cert),
        ("ciqRegNo", "91330200123456789X"),
        ("aplRegNo", "91330200123456789X"),
        ("etpsName", "联调测试企业（非真实业务）"),
        ("applName", "联调测试"),
        ("applicant", "110101199001010000"),
        ("applTel", "01000000000"),
        ("orgCode", "3100"),
        ("fetchPlace", "3100"),
        ("aplAdd", "NINGBO, CHINA"),
        ("aplDate", "2026-11-09"),
        ("exporter", "INTEGRATION TEST EXPORTER\nNINGBO, CHINA"),
        ("consignee", "DESTINATION RECEIVER\nTEST ADDRESS"),
        ("mark", "TEST ONLY"),
        ("aplPromiseCode", "1"),
        ("invDate", "2026-11-09"),
        ("invNo", "SW-TEST-20261109"),
        ("curr", "USD"),
        ("priceTerms", "FOB"),
        ("totalAmt", "100"),
        ("transDetails", "FROM NINGBO TO DESTINATION BY SEA"),
    ] {
        doc[key] = json!(value);
    }
    doc["certNo"] = json!(format!("{cert}26123456789{sequence}"));
    for (key, field) in [
        ("destCountry", "englishName"),
        ("destCountryCode", "code"),
        ("destCountryName", "chineseName"),
    ] {
        doc[key] = json!(catalog::resolve(
            catalog::defaults(),
            "countries",
            country,
            field
        ));
    }
    let mut item = contracts::initial(contracts::schema("ApiCustomsCooItemDto"));
    item["gNo"] = json!(1);
    for (key, value) in [
        ("goodsItemFlag", "N"),
        ("hsCode", "6109100010"),
        ("goodsName", "棉制针织衫"),
        ("goodsNameE", "100% COTTON KNITTED T-SHIRTS, ADULT"),
        ("packQty", "1"),
        ("packUnit", "CARTON"),
        ("packType", "1"),
        ("goodsQty", "10"),
        ("goodsUnitE", "PIECES"),
        ("goodsUnit", "件"),
        ("invValue", "100"),
        ("fobValue", "100"),
        ("goodsDesc", "ONE CARTON OF COTTON T-SHIRTS"),
        ("ciqRegNo", "123456789"),
        ("prdcEtpsName", "联调测试生产企业"),
        ("prdcEtpsConcEr", "测试"),
        ("prdcEtpsTel", "01000000000"),
        ("invNo", "SW-TEST-20261109"),
        ("goodsOriginCountry", "156"),
        ("goodsOriginCountryEn", "CHINA"),
    ] {
        item[key] = json!(value);
    }
    item["oriCriteria"] = json!(match cert {
        "C" => "",
        "G" => "P",
        "E" => "PE",
        "RC" => "PE",
        _ => "WP",
    });
    doc["items"] = json!([item]);
    doc
}

pub const COMMON: [(&str, &str, &str); 6] = [
    ("C", "JAPAN", "9001"),
    ("G", "JAPAN", "9002"),
    ("A", "AUSTRALIA", "9003"),
    ("E", "THAILAND", "9004"),
    ("RC", "JAPAN", "9005"),
    ("K", "KOREA", "9006"),
];
