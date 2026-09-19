use super::{Business, catalog, reference, text};
use crate::{contracts, number::english_integer};
use rust_decimal::{Decimal, prelude::ToPrimitive};
use serde_json::{Value, json};
fn first<'a>(values: &[&'a str]) -> &'a str {
    values
        .iter()
        .copied()
        .find(|v| !v.trim().is_empty())
        .unwrap_or("")
        .trim()
}
pub fn decimal(value: &Value, digits: u32) -> String {
    let number = value
        .as_str()
        .map(str::to_owned)
        .unwrap_or_else(|| value.to_string())
        .parse::<Decimal>()
        .unwrap_or_default();
    if number.is_zero() {
        String::new()
    } else {
        number.round_dp(digits).normalize().to_string()
    }
}
fn phone(value: &str) -> String {
    value
        .chars()
        .filter(|c| c.is_alphanumeric() || matches!(c, '+' | '-' | '(' | ')' | '#'))
        .collect()
}
pub fn unit(value: &str, english: bool) -> String {
    let key: String = value
        .to_uppercase()
        .chars()
        .filter(|c| c.is_alphanumeric())
        .collect();
    reference()["units"][if english {
        "UnitEnglishLookup"
    } else {
        "UnitChineseLookup"
    }][key]
        .as_str()
        .map(str::to_owned)
        .unwrap_or_else(|| {
            if english {
                value.trim().to_uppercase()
            } else {
                value.trim().into()
            }
        })
}
pub fn party(name: &str, address: &str) -> String {
    let mut lines: Vec<String> = name
        .trim()
        .is_empty()
        .then(Vec::new)
        .unwrap_or_else(|| vec![name.trim().into()]);
    let normalized = address
        .replace("/n", "\n")
        .replace("/N", "\n")
        .replace("\r\n", "\n")
        .replace('\r', "\n");
    let explicit: Vec<_> = normalized
        .lines()
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .collect();
    if explicit.len() > 1 {
        lines.extend(explicit.into_iter().take(2).map(str::to_owned));
    } else {
        let segments: Vec<_> = normalized
            .split([',', '，', ';', '；'])
            .map(str::trim)
            .filter(|s| !s.is_empty())
            .collect();
        if segments.len() <= 2 {
            lines.extend(segments.into_iter().map(str::to_owned));
        } else {
            let total: usize = segments.iter().map(|s| s.chars().count()).sum();
            let mut length = 0;
            let mut left = Vec::new();
            let mut right = Vec::new();
            for segment in segments {
                if left.is_empty()
                    || (length + segment.chars().count() <= total / 2 && right.is_empty())
                {
                    left.push(segment);
                    length += segment.chars().count();
                } else {
                    right.push(segment);
                }
            }
            if right.is_empty() && left.len() > 1 {
                right.push(left.pop().unwrap());
            }
            lines.push(left.join(", "));
            if !right.is_empty() {
                lines.push(right.join(", "));
            }
        }
    }
    lines.join("\n")
}
fn customs_code(value: &str) -> &str {
    if value.len() == 10 && value.bytes().all(|c| c.is_ascii_digit()) {
        value
    } else {
        ""
    }
}
pub fn cert_no(
    current: &str,
    cert_type: &str,
    primary: &str,
    secondary: &str,
    date: &str,
    today: &str,
) -> String {
    let kind = cert_type.trim().to_uppercase();
    let code = [primary, secondary]
        .into_iter()
        .map(|s| s.trim().to_uppercase())
        .find_map(|s| {
            if s.is_ascii() && s.bytes().all(|c| c.is_ascii_alphanumeric()) {
                match s.len() {
                    18 => Some(s[8..17].to_owned()),
                    9 => Some(s),
                    _ => None,
                }
            } else {
                None
            }
        });
    let current = current.trim().to_uppercase();
    let Some(code) = code.filter(|_| (1..=2).contains(&kind.len())) else {
        return current;
    };
    let date = if crate::invoice::valid_date(date) {
        date
    } else {
        today
    };
    let sequence = if current.is_empty() {
        "0001"
    } else if current.len() == 4 && current.bytes().all(|c| c.is_ascii_digit()) {
        &current
    } else if (16..=17).contains(&current.len())
        && current.is_ascii()
        && current.bytes().all(|c| c.is_ascii_alphanumeric())
        && current[current.len() - 4..]
            .bytes()
            .all(|c| c.is_ascii_digit())
    {
        &current[current.len() - 4..]
    } else {
        return current;
    };
    format!("{kind}{}{code}{sequence}", &date[2..4])
}
pub fn defaults(
    business: Business,
    invoice: &Value,
    exporter: &Value,
    customer: &Value,
    catalog: &Value,
    preferences: &Value,
    today: &str,
) -> Value {
    let mut out = business.blank();
    out["sourceInvoiceId"] = invoice["id"].clone();
    out["invoiceNo"] = invoice["invoiceNo"].clone();
    out["contractNo"] = invoice["contractNo"].clone();
    out["status"] = json!("Draft");
    out["lastGeneratedAt"] = Value::Null;
    if business == Business::Acd {
        acd(&mut out, invoice, exporter, catalog, today);
        return out;
    }
    for (key, value) in [
        ("applyType", "0"),
        ("certStatus", "0"),
        ("certType", "C"),
        ("aplPromiseCode", "1"),
    ] {
        out[key] = json!(value);
    }
    for (key, source) in [
        ("invNo", "invoiceNo"),
        ("invDate", "invoiceDate"),
        ("goodsSpecClause", "specialTerms"),
        ("mark", "shippingMarks"),
        ("intendExpDate", "shipmentDate"),
        ("note", "specialTerms"),
        ("lcNo", "letterOfCreditNo"),
        ("specInvTerms", "specialTerms"),
        ("remark", "specialTerms"),
    ] {
        out[key] = json!(text(invoice, source));
    }
    out["aplDate"] = json!(today);
    out["fobValue"] = json!(decimal(&invoice["totalAmount"], 2));
    out["totalAmt"] = out["fobValue"].clone();
    let credit = first(&[
        text(invoice, "exporterCreditCode"),
        text(exporter, "creditCode"),
    ]);
    out["ciqRegNo"] = json!(credit);
    out["aplRegNo"] = json!(credit);
    out["etpsName"] = json!(first(&[
        text(invoice, "exporterNameCN"),
        text(exporter, "exporterNameCN")
    ]));
    out["exporter"] = json!(party(
        first(&[
            text(invoice, "exporterNameEN"),
            text(exporter, "exporterNameEN")
        ]),
        first(&[
            text(invoice, "exporterAddressEN"),
            text(exporter, "addressEN")
        ])
    ));
    out["consignee"] = json!(party(
        first(&[
            text(invoice, "customerNameEN"),
            text(customer, "customerNameEN")
        ]),
        first(&[
            text(invoice, "customerAddressEN"),
            text(customer, "addressEN")
        ])
    ));
    let destination = text(invoice, "destinationCountry");
    for (key, field) in [
        ("destCountry", "englishName"),
        ("destCountryCode", "code"),
        ("destCountryName", "chineseName"),
    ] {
        out[key] = json!(
            catalog::find(catalog, "countries", destination)
                .map(|r| text(r, field).to_owned())
                .unwrap_or_else(|| if field == "code" {
                    if destination.len() <= 4 && destination.bytes().all(|c| c.is_ascii_digit()) {
                        destination.into()
                    } else {
                        String::new()
                    }
                } else {
                    destination.to_uppercase()
                })
        );
    }
    for (key, source) in [
        ("loadPort", "portOfLoading"),
        ("unloadPort", "portOfDestination"),
        ("destPort", "portOfDestination"),
    ] {
        out[key] = json!(
            catalog::find(catalog, "ports", text(invoice, source))
                .map(|r| text(r, "value").into())
                .unwrap_or_else(|| text(invoice, source).to_uppercase())
        );
    }
    out["transMeans"] = json!(
        catalog::find(catalog, "transportModes", text(invoice, "transportMode"))
            .map(|r| text(r, "value").into())
            .unwrap_or_else(|| text(invoice, "transportMode").to_uppercase())
    );
    out["transDetails"] = json!(
        [
            ("FROM", text(&out, "loadPort")),
            ("TO", text(&out, "unloadPort")),
            ("", text(&out, "transMeans"))
        ]
        .into_iter()
        .filter(|(_, s)| !s.is_empty())
        .map(|(p, s)| format!("{p} {s}").trim().to_owned())
        .collect::<Vec<_>>()
        .join(" ")
    );
    let terms = text(invoice, "tradeTerms").to_uppercase();
    out["priceTerms"] = json!(terms.split([' ', '(']).next().unwrap_or(""));
    let currency = catalog::find(catalog, "currencies", text(invoice, "currency"));
    out["curr"] = json!(currency.map(|c| text(c, "alphaCode")).unwrap_or(""));
    let trade = text(invoice, "supervisionMode");
    out["tradeModeCode"] = json!(reference()["cooTradeModes"][trade].as_str().unwrap_or_else(
        || {
            if reference()["options"]["cooTradeModeOptions"]
                .as_array()
                .into_iter()
                .flatten()
                .any(|r| r["value"] == trade)
            {
                trade
            } else {
                ""
            }
        }
    ));
    out["exporterTel"] = json!(phone(text(exporter, "phone")));
    out["etpsTel"] = out["exporterTel"].clone();
    out["etpsConcEr"] = json!(text(exporter, "contactPerson"));
    out["consigneeTel"] = json!(phone(text(customer, "phone")));
    out["consigneeEmail"] = json!(text(customer, "email"));
    if text(&out, "mark").is_empty() {
        out["mark"] = json!("N/M");
    }
    let first_item = &invoice["items"][0];
    out["oriCountryCode"] = json!(catalog::resolve(
        catalog,
        "countries",
        text(first_item, "origin"),
        "code"
    ));
    out["oriCountry"] = json!(catalog::resolve(
        catalog,
        "countries",
        text(first_item, "origin"),
        "englishName"
    ));
    for key in [
        "applName",
        "applicant",
        "applTel",
        "orgCode",
        "fetchPlace",
        "aplAdd",
    ] {
        if !text(preferences, key).is_empty() {
            out[key] = json!(text(preferences, key));
        }
    }
    if text(&out, "fetchPlace").is_empty() {
        out["fetchPlace"] = out["orgCode"].clone();
    }
    if text(&out, "aplAdd").is_empty() {
        out["aplAdd"] = json!(
            catalog::authorities()["options"]
                .as_array()
                .into_iter()
                .flatten()
                .find(|a| a["code"] == out["orgCode"])
                .map(|a| text(a, "applicationAddress"))
                .unwrap_or("")
        );
    }
    out["certNo"] = json!(cert_no(
        "",
        text(&out, "certType"),
        credit,
        credit,
        today,
        today
    ));
    out["items"] = json!(
        invoice["items"]
            .as_array()
            .into_iter()
            .flatten()
            .enumerate()
            .map(|(i, item)| goods(item, invoice, exporter, catalog, i + 1))
            .collect::<Vec<_>>()
    );
    out
}
fn acd(out: &mut Value, invoice: &Value, exporter: &Value, catalog: &Value, today: &str) {
    let item = &invoice["items"][0];
    let customs = first(&[
        customs_code(text(invoice, "exporterCustomsCode")),
        customs_code(text(exporter, "customsCode")),
    ]);
    out["copCusCode"] = json!(customs);
    out["tradeCode"] = json!(customs);
    out["operType"] = json!("1");
    out["agentCode"] = json!(customs_code(text(invoice, "customsBrokerCode")));
    out["gName"] = json!(first(&[text(item, "styleNameCN"), text(item, "styleName")]));
    out["codeTS"] = json!(text(item, "hsCode"));
    out["declTotal"] = json!(decimal(&invoice["totalAmount"], 4));
    out["ieDate"] = json!(
        first(&[text(invoice, "shipmentDate"), text(invoice, "invoiceDate")]).replace('-', "")
    );
    out["receiveDate"] = json!(today.replace('-', ""));
    let mode = text(invoice, "supervisionMode");
    out["tradeMode"] = json!(
        if mode.len() == 4 && mode.bytes().all(|b| b.is_ascii_digit()) {
            mode.into()
        } else {
            catalog::resolve(catalog, "acdTradeModes", mode, "code")
        }
    );
    let origin = text(item, "origin");
    let resolved = catalog::resolve(catalog, "acdCountries", origin, "code");
    out["oriCountry"] = json!(if !resolved.is_empty() {
        resolved
    } else if origin.len() == 3 && origin.bytes().all(|b| b.is_ascii_digit()) {
        origin.into()
    } else {
        "142".into()
    });
    out["curr"] = json!(catalog::resolve(
        catalog,
        "currencies",
        text(invoice, "currency"),
        "acdCode"
    ));
    out["qtyOrWeight"] = json!(first(&[
        &decimal(&invoice["totalGrossWeight"], 2),
        &decimal(&invoice["totalQuantity"], 2)
    ]));
    out["packingCondition"] = json!(text(invoice, "specialTerms"));
    out["otherNote"] = out["packingCondition"].clone();
    out["consignTele"] = json!(phone(text(exporter, "phone")));
}
fn goods(item: &Value, invoice: &Value, exporter: &Value, catalog: &Value, index: usize) -> Value {
    let mut row = contracts::initial(contracts::schema("ApiCustomsCooItemDto"));
    row["sourceItemId"] = item["id"].clone();
    row["sourceStyleNo"] = json!(text(item, "styleNo"));
    row["gNo"] = json!(index);
    for (key, source) in [
        ("hsCode", "hsCode"),
        ("goodsName", "styleNameCN"),
        ("goodsNameE", "styleName"),
    ] {
        row[key] = json!(text(item, source));
    }
    for (key, source) in [
        ("packQty", "cartons"),
        ("goodsQty", "quantity"),
        ("goodsQtyRef", "quantity"),
        ("secdGoodsQtyRef", "cartons"),
        ("grossWt", "gwTotal"),
        ("netWt", "nwTotal"),
        ("invValue", "totalPrice"),
        ("fobValue", "totalPrice"),
    ] {
        row[key] = json!(decimal(&item[source], 2));
    }
    row["invPrice"] = json!(decimal(&item["unitPrice"], 5));
    row["goodsItemFlag"] = json!("N");
    row["packType"] = json!("1");
    row["wtUnit"] = json!("KGS");
    row["packUnit"] = json!(unit(text(item, "ctnUnitEN"), true));
    row["secdGoodsUnitRef"] = row["packUnit"].clone();
    row["goodsUnitE"] = json!(unit(text(item, "unitEN"), true));
    row["goodsUnitRef"] = row["goodsUnitE"].clone();
    row["goodsUnit"] = json!(unit(
        first(&[text(item, "unitCN"), text(item, "unitEN")]),
        false
    ));
    row["goodsOriginCountry"] = json!(catalog::resolve(
        catalog,
        "countries",
        text(item, "origin"),
        "code"
    ));
    row["goodsOriginCountryEn"] = json!(catalog::resolve(
        catalog,
        "countries",
        text(item, "origin"),
        "englishName"
    ));
    row["prdcEtpsName"] = json!(first(&[
        text(invoice, "exporterNameCN"),
        text(exporter, "exporterNameCN")
    ]));
    row["prdcEtpsConcEr"] = json!(text(exporter, "contactPerson"));
    row["prdcEtpsTel"] = json!(phone(text(exporter, "phone")));
    row["invNo"] = json!(text(invoice, "invoiceNo"));
    row["goodsDesc"] = json!(goods_description(&row));
    row
}
pub fn goods_description(row: &Value) -> String {
    let quantity = text(row, "packQty");
    let name = first(&[text(row, "goodsNameE"), text(row, "goodsName")]).to_uppercase();
    let units = unit(text(row, "packUnit"), true);
    if quantity.is_empty() || name.is_empty() || units.is_empty() {
        return String::new();
    }
    let number = quantity.parse::<Decimal>().ok();
    let singular = number == Some(Decimal::ONE);
    let units = match units.as_str() {
        "CTN" => {
            if singular {
                "CARTON"
            } else {
                "CARTONS"
            }
        }
        "PCS" => {
            if singular {
                "PIECE"
            } else {
                "PIECES"
            }
        }
        "SET" => {
            if singular {
                "SET"
            } else {
                "SETS"
            }
        }
        "BOX" => {
            if singular {
                "BOX"
            } else {
                "BOXES"
            }
        }
        "KGS" => {
            if singular {
                "KILOGRAM"
            } else {
                "KILOGRAMS"
            }
        }
        _ => &units,
    };
    let quantity = number
        .filter(|n| n.fract().is_zero() && *n > Decimal::ZERO)
        .and_then(|n| n.to_u64())
        .filter(|n| *n <= i32::MAX as u64)
        .map(|n| format!("{} ({n})", english_integer(n)))
        .unwrap_or_else(|| quantity.into());
    format!("{quantity} {units} OF {name}")
}
