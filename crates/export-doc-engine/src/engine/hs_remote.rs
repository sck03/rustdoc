use super::{
    NativeService,
    error::{Result, invalid, unavailable},
    hs, hs_search,
    records::text,
    store::{self, Actor},
};
use crate::{generated_api::*, operation};
use export_doc_domain::hs as rules;
use serde_json::{Value, json};
pub const OPERATIONS: &[Operation] = &[
    SEARCH_REMOTE_HS_CODES,
    CAPTURE_REMOTE_HS_CODES,
    FETCH_REMOTE_HS_CODE_DETAIL,
    RESOLVE_REMOTE_HS_CODE_DETAIL,
    GET_HS_CODE_REMOTE_HEALTH,
];
fn check() -> std::result::Result<(), String> {
    operation::check().map_err(|e| e.to_string())
}
pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    query: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    let observed = store::timestamp();
    if operation == GET_HS_CODE_REMOTE_HEALTH {
        let health = export_doc_hs::health(&check);
        operation::check()?;
        return Ok(
            json!({"source":export_doc_hs::SOURCE,"available":health.is_ok(),"checkedAt":observed,"message":health.err().unwrap_or_else(||"静态参考页面可访问。".into())}),
        );
    }
    if [SEARCH_REMOTE_HS_CODES, CAPTURE_REMOTE_HS_CODES].contains(&operation) {
        let keyword = if operation == SEARCH_REMOTE_HS_CODES {
            hs::read(query, "keyword").to_string()
        } else {
            text(body, "keyword")
        };
        if keyword.trim().is_empty() || keyword.chars().count() > 500 {
            return Err(invalid("请输入 1 至 500 字的检索条件。"));
        }
        let items = export_doc_hs::search(&keyword, &observed, &check).map_err(unavailable)?;
        operation::check()?;
        if operation == CAPTURE_REMOTE_HS_CODES {
            capture(service, actor, &keyword, &items)?;
        }
        Ok(
            json!({"count":items.len(),"standardCodeCount":items.iter().filter(|r|r.remote_record_kind=="StandardCode").count(),"declarationExampleCount":items.iter().filter(|r|r.remote_record_kind=="DeclarationExample").count(),"items":items,"source":export_doc_hs::SOURCE,"storagePolicy":"第三方资料只作参考；申报案例须审核后进入公司知识库。"}),
        )
    } else {
        let seed: ApiHsCodeDto =
            serde_json::from_value(body.clone()).map_err(|e| invalid(e.to_string()))?;
        export_doc_hs::trusted_url(&seed.detail_url).map_err(invalid)?;
        let detail = export_doc_hs::detail(&seed, &observed, &check).map_err(unavailable)?;
        operation::check()?;
        if operation == FETCH_REMOTE_HS_CODE_DETAIL {
            return Ok(serde_json::to_value(detail)?);
        }
        if detail.status == "Obsolete" {
            let mut items = vec![];
            for keyword in detail
                .recommended_keywords
                .as_ref()
                .and_then(Value::as_array)
                .into_iter()
                .flatten()
                .filter_map(Value::as_str)
                .take(3)
            {
                operation::check()?;
                items.extend(
                    export_doc_hs::search(keyword, &observed, &check).map_err(unavailable)?,
                );
            }
            let count = items.len();
            Ok(
                json!({"items":items,"removedItems":[detail],"updatedCount":count,"removedCount":1,"message":"作废参考已移出候选，并读取可用的推荐资料。","storagePolicy":"本地税则及历史单据未更改。"}),
            )
        } else {
            Ok(
                json!({"items":[detail],"removedItems":[],"updatedCount":1,"removedCount":0,"message":"参考详情已读取。","storagePolicy":"参考资料未写入本地税则。"}),
            )
        }
    }
}
fn capture(
    service: &NativeService,
    actor: &Actor,
    query: &str,
    items: &[ApiHsCodeDto],
) -> Result<()> {
    service.store.transaction(|tx|{
        let codes=hs::codes(tx)?;let relations=store::all(tx,hs::REPLACEMENTS)?;
        for item in items.iter().filter(|r|r.remote_record_kind=="DeclarationExample"){
            operation::check()?;let raw=rules::code(&item.code).map_err(invalid)?;
            let name=export_doc_domain::crm::text(&item.name,"参考商品名称",300,true).map_err(invalid)?;
            let spec=export_doc_domain::crm::text(&item.elements,"参考规格",1500,false).map_err(invalid)?;
            let fingerprint=rules::fingerprint(&[&raw,&name,&spec]);let previous=tx.find_identity(hs::CANDIDATES,&fingerprint.to_lowercase())?;
            let id=previous.as_ref().and_then(|r|r["id"].as_i64()).unwrap_or(0);let resolved=hs_search::resolve(&raw,"",false,&codes,&relations);
            let now=store::timestamp();let mut row=previous.unwrap_or_else(||json!({"fingerprint":fingerprint,"rawReportedHsCode":raw,"productName":name,"specification":spec,"source":export_doc_hs::SOURCE,"sourceUrl":item.evidence_url,"reviewStatus":"Pending","firstSeenAt":now,"reviewedAt":null,"seenCount":0}));
            row["queryText"]=json!(query);row["lastSeenAt"]=json!(now);row["seenCount"]=json!(row["seenCount"].as_i64().unwrap_or(0).saturating_add(1));
            if row["reviewStatus"]=="Pending"{row["suggestedCurrentHsCode"]=json!(resolved.code);row["resolutionStatus"]=json!(resolved.status);}
            store::save(tx,hs::CANDIDATES,id,row,Some(fingerprint),actor,if id==0{"create"}else{"edit"})?;
        }
        Ok(())
    })
}
