use super::*;
use rules::{draft, validation};
use std::collections::{BTreeMap, BTreeSet};
pub fn group(scope: &str, field: &str) -> &'static str {
    if scope == "goods" {
        return "明细项目";
    }
    if scope == "corp" {
        return "非缔约方企业";
    }
    if scope == "acd" {
        return if ["copCusCode", "sign", "operType"].contains(&field) {
            "基础标识"
        } else if ["entryId", "consignNo"].contains(&field) {
            "回执回写信息"
        } else if [
            "paperInfo",
            "otherRecInfo",
            "declarePrice",
            "receiveDate",
            "promiseNote",
            "declTele",
        ]
        .contains(&field)
        {
            "单证与费用"
        } else {
            "申报要素"
        };
    }
    if [
        "applyType",
        "certStatus",
        "certType",
        "certNo",
        "entMgrNo",
        "ciqRegNo",
        "aplRegNo",
        "etpsName",
    ]
    .contains(&field)
    {
        "证书基础"
    } else if [
        "applName",
        "applicant",
        "applTel",
        "orgCode",
        "fetchPlace",
        "aplAdd",
        "aplDate",
        "invNo",
        "invDate",
        "destCountry",
        "destCountryCode",
        "destCountryName",
        "exporter",
        "consignee",
        "exporterTel",
        "exporterFax",
        "exporterEmail",
        "consigneeTel",
        "consigneeFax",
        "consigneeEmail",
    ]
    .contains(&field)
    {
        "申报与对象"
    } else if [
        "loadPort",
        "unloadPort",
        "transMeans",
        "transName",
        "transCountryCode",
        "transCountryName",
        "transPort",
        "destPort",
        "transDetails",
        "intendExpDate",
        "tradeModeCode",
        "fobValue",
        "totalAmt",
        "curr",
        "priceTerms",
        "goodsSpecClause",
        "mark",
        "note",
        "lcNo",
        "specInvTerms",
    ]
    .contains(&field)
    {
        "运输与贸易"
    } else {
        "补充与特殊项"
    }
}
pub fn build(business: Business, current: &Value, defaults: &Value) -> Value {
    let issues = validation::review(business, current);
    let mut groups: BTreeMap<&str, Vec<Value>> = BTreeMap::new();
    for issue in &issues {
        let name = group(issue.scope, &issue.field);
        let suggested = issue
            .row
            .map(|i| {
                &defaults[if issue.scope == "corp" {
                    "nonpartyCorps"
                } else {
                    "items"
                }][i]
            })
            .unwrap_or(defaults);
        let repair = !rules::text(suggested, &issue.field).is_empty();
        groups.entry(name).or_default().push(json!({"groupKey":name,"groupDisplayName":name,"message":issue.message,"severity":"Error","canAutoRepair":repair,"navigationTarget":{"groupKey":name,"propertyKey":rules::pascal(&issue.field),"goodsLineNo":issue.row.map(|r|r+1)}}));
    }
    let groups:Vec<_>=groups.into_iter().map(|(key,issues)|json!({"groupKey":key,"groupDisplayName":key,"canAutoRepair":issues.iter().any(|i|i["canAutoRepair"]==true),"errorCount":issues.len(),"warningCount":0,"infoCount":0,"issues":issues})).collect();
    json!({"businessType":business.name(),"invoiceId":current["sourceInvoiceId"],"invoiceNo":current["invoiceNo"],"contractNo":current["contractNo"],"draftRevision":current["draftRevision"],"manualLockedFieldCount":current["manualLockedFieldCount"],"sourceDiffCount":current["sourceDiffCount"],"sourceDiffSummary":current["sourceDiffSummary"],"groups":groups,"totalErrorCount":issues.len(),"totalWarningCount":0,"hasIssues":!issues.is_empty()})
}
pub fn repair(
    business: Business,
    loaded: &mut documents::Loaded,
    groups: &[String],
) -> Result<usize> {
    if groups.is_empty() || groups.len() > 10 {
        return Err(invalid("请选择需要修复的有效分组。"));
    }
    let issues = validation::review(business, &loaded.current);
    let mut changed = BTreeSet::new();
    let mut keys = BTreeSet::new();
    for issue in issues {
        let name = group(issue.scope, &issue.field);
        if !groups.iter().any(|g| g == name) {
            continue;
        }
        let suggested = issue
            .row
            .map(|i| {
                &loaded.defaults[if issue.scope == "corp" {
                    "nonpartyCorps"
                } else {
                    "items"
                }][i]
            })
            .unwrap_or(&loaded.defaults);
        if rules::text(suggested, &issue.field).is_empty() {
            continue;
        }
        let key = if let Some(index) = issue.row {
            format!(
                "Goods:{}:{}",
                draft::identity(&loaded.current["items"][index]),
                rules::pascal(&issue.field)
            )
        } else {
            rules::pascal(&issue.field)
        };
        keys.insert(key);
        changed.insert(name);
    }
    // Keep every field outside the selected issue set, including manual blanks.
    let retained = draft::fields(business, &loaded.current)
        .keys()
        .filter(|key| !keys.contains(*key))
        .cloned()
        .collect();
    let mut result = loaded.defaults.clone();
    draft::restore_locked(business, &mut result, &loaded.current, &retained);
    loaded.current = result;
    Ok(changed.len())
}
