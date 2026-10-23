//! Specialized reads share scope, SQL primitives and the provider's page snapshot.
use crate::{
    DataScope, QuerySql, Result,
    sql::{self, bind, field},
};
use serde_json::Value;

pub enum BusinessList<'a> {
    Products {
        keyword: &'a str,
    },
    Deliveries {
        keyword: &'a str,
        status: &'a str,
    },
    Attachments {
        invoice_id: Option<i64>,
        category_id: Option<i64>,
        archived: bool,
        keyword: &'a str,
        status: &'a str,
    },
    Invoices {
        filter: &'a Value,
        tokens: &'a [String],
    },
    Dashboard,
    InvoiceIds(&'a [i64]),
}
pub struct BusinessQuery<'a> {
    pub list: BusinessList<'a>,
    pub scope: DataScope<'a>,
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}

fn any_text(
    paths: &[&str],
    term: &str,
    values: &mut Vec<String>,
    pg: bool,
    prefix: bool,
    item: bool,
) -> String {
    let parameter = bind(values, term, pg);
    let tests = paths
        .iter()
        .map(|path| {
            let expression = field(path, pg);
            let expression = if item {
                expression.replace("r.body", "item.value")
            } else {
                expression
            };
            if prefix {
                let expression = sql::normalized(&format!("COALESCE({expression},'')"), pg);
                format!("substr({expression},1,length({parameter}))={parameter}")
            } else {
                sql::contains(&expression, &parameter, pg)
            }
        })
        .collect::<Vec<_>>()
        .join(" OR ");
    format!("({tests})")
}
fn items(test: String, pg: bool) -> String {
    let source = if pg {
        "jsonb_array_elements(COALESCE(r.body->'items','[]'::jsonb)) AS item(value)"
    } else {
        "json_each(r.body,'$.items') AS item"
    };
    format!("EXISTS (SELECT 1 FROM {source} WHERE {test})")
}
pub(crate) fn build(q: &BusinessQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    let mut values = Vec::new();
    let scope = q.scope.filter(&mut values, pg);
    let mut clauses = vec![];
    let order;
    match &q.list {
        BusinessList::Products { keyword } => {
            clauses.extend(["r.kind='products'".into(), scope]);
            if !keyword.is_empty() {
                clauses.push(any_text(
                    &["productCode", "nameCN", "nameEN"],
                    keyword,
                    &mut values,
                    pg,
                    false,
                    false,
                ));
            }
            order = if pg {
                "r.body->>'productCode' COLLATE \"C\", r.body->>'nameCN' COLLATE \"C\", r.id"
            } else {
                "json_extract(r.body,'$.productCode') COLLATE BINARY, json_extract(r.body,'$.nameCN') COLLATE BINARY, r.id"
            };
        }
        BusinessList::Deliveries { keyword, status } => {
            clauses.extend(["r.kind='email-deliveries'".into(), scope]);
            if !status.is_empty() {
                clauses.push(format!(
                    "{}={}",
                    field("status", pg),
                    bind(&mut values, status, pg)
                ));
            }
            if !keyword.is_empty() {
                clauses.push(any_text(
                    &["recipient", "subject"],
                    keyword,
                    &mut values,
                    pg,
                    false,
                    false,
                ));
            }
            order = if pg {
                "r.body->>'createdAt' DESC, r.id DESC"
            } else {
                "json_extract(r.body,'$.createdAt') DESC, r.id DESC"
            };
        }
        BusinessList::Attachments {
            invoice_id,
            category_id,
            archived,
            keyword,
            status,
        } => {
            clauses.push("r.kind='attachments'".into());
            // Apply invoice scope inside the correlated lookup, before count/page.
            let invoice_scope = scope.replace("r.", "invoice.");
            clauses.push(format!("EXISTS (SELECT 1 FROM records invoice WHERE invoice.kind='invoices' AND invoice.id=CAST({} AS BIGINT) AND {invoice_scope})",field("invoiceId",pg)));
            for (path, id) in [("invoiceId", invoice_id), ("categoryId", category_id)] {
                if let Some(id) = id {
                    clauses.push(format!(
                        "CAST({} AS BIGINT)=CAST({} AS BIGINT)",
                        field(path, pg),
                        bind(&mut values, id, pg)
                    ));
                }
            }
            if !archived {
                clauses.push(
                    if pg {
                        "COALESCE((r.body->>'isArchived')::boolean,false)=false"
                    } else {
                        "COALESCE(json_extract(r.body,'$.isArchived'),0)<>1"
                    }
                    .into(),
                );
            }
            if !status.is_empty() {
                clauses.push(format!(
                    "{}={}",
                    field("status", pg),
                    bind(&mut values, status, pg)
                ));
            }
            if !keyword.is_empty() {
                let category_id = field("categoryId", pg);
                let category = format!(
                    "SELECT category.body FROM records category WHERE category.kind='attachment-categories' AND category.id=CAST({category_id} AS BIGINT)"
                );
                let root = if pg {
                    format!(
                        "exportdoc_record_text(jsonb_set(r.body,'{{categoryName}}',COALESCE(({category})->'name',r.body->'categoryName','null'::jsonb)))"
                    )
                } else {
                    format!(
                        "record_json(json_set(r.body,'$.categoryName',COALESCE(json_extract(({category}),'$.name'),json_extract(r.body,'$.categoryName'))))"
                    )
                };
                let parameter = bind(&mut values, keyword, pg);
                clauses.push(sql::contains(&root, &parameter, pg));
            }
            // Previous list kept storage's ascending record order.
            order = "r.id";
        }
        BusinessList::Invoices { filter, tokens } => {
            clauses.extend(["r.kind='invoices'".into(), scope]);
            let text = |key: &str| filter[key].as_str().unwrap_or("");
            for (key, path) in [("customerId", "customerId"), ("exporterId", "exporterId")] {
                if let Some(id) = filter[key].as_i64().filter(|id| *id > 0) {
                    clauses.push(format!(
                        "CAST({} AS BIGINT)=CAST({} AS BIGINT)",
                        field(path, pg),
                        bind(&mut values, id, pg)
                    ));
                }
            }
            for (key, path) in [("invoiceType", "type"), ("transportMode", "transportMode")] {
                if !text(key).is_empty() {
                    clauses.push(format!(
                        "{}={}",
                        sql::normalized(&field(path, pg), pg),
                        bind(&mut values, text(key), pg)
                    ));
                }
            }
            for (key, operator) in [("startDate", ">="), ("endDateExclusive", "<")] {
                if !text(key).is_empty() {
                    let date = field("shipmentDate", pg);
                    let date = if pg {
                        format!("CAST(COALESCE({date},'') AS DATE)")
                    } else {
                        format!("record_date({date})")
                    };
                    let parameter = bind(&mut values, text(key), pg);
                    clauses.push(format!(
                        "{date}{operator}{}",
                        if pg {
                            format!("CAST({parameter} AS DATE)")
                        } else {
                            parameter
                        }
                    ));
                }
            }
            if !text("contractNo").is_empty() && text("contractNo") != text("keyword") {
                clauses.push(any_text(
                    &["contractNo"],
                    text("contractNo"),
                    &mut values,
                    pg,
                    false,
                    false,
                ));
            }
            for (key, path) in [("styleName", "styleName"), ("styleNo", "styleNo")] {
                if !text(key).is_empty() {
                    clauses.push(items(
                        any_text(&[path], text(key), &mut values, pg, false, true),
                        pg,
                    ));
                }
            }
            for token in *tokens {
                let prefix = token.chars().any(char::is_numeric);
                let header = if prefix {
                    vec!["invoiceNo", "contractNo"]
                } else {
                    vec![
                        "invoiceNo",
                        "contractNo",
                        "customerNameEN",
                        "notifyPartyName",
                        "exporterNameEN",
                        "exporterNameCN",
                        "destinationCountry",
                        "portOfLoading",
                        "portOfDestination",
                        "tradeTerms",
                        "transportMode",
                    ]
                };
                let detail = if prefix {
                    vec!["poNumber", "styleNo", "hsCode"]
                } else {
                    vec![
                        "poNumber",
                        "styleNo",
                        "hsCode",
                        "styleName",
                        "styleNameCN",
                        "brand",
                        "origin",
                    ]
                };
                let header = any_text(&header, token, &mut values, pg, prefix, false);
                let detail = items(any_text(&detail, token, &mut values, pg, prefix, true), pg);
                clauses.push(format!("({header} OR {detail})"));
            }
            order = if pg {
                "r.body->>'invoiceDate' DESC, r.id DESC"
            } else {
                "json_extract(r.body,'$.invoiceDate') DESC, r.id DESC"
            };
        }
        BusinessList::Dashboard => {
            clauses.extend(["r.kind='invoices'".into(), scope]);
            clauses.push(format!("{}<>'Cancelled'", field("status", pg)));
            order = "r.id DESC";
        }
        BusinessList::InvoiceIds(ids) => {
            clauses.extend(["r.kind='invoices'".into(), scope]);
            let ids = ids
                .iter()
                .map(|id| format!("CAST({} AS BIGINT)", bind(&mut values, id, pg)))
                .collect::<Vec<_>>();
            clauses.push(if ids.is_empty() {
                "1=0".into()
            } else {
                format!("r.id IN ({})", ids.join(","))
            });
            order = "r.id";
        }
    }
    Ok((
        QuerySql {
            filter: clauses.join(" AND "),
            order,
            values,
        },
        sql::projection(q.fields, pg)?,
    ))
}
