//! CRM reads apply record and parent scopes before paging and only project requested fields.
use crate::{DataScope, QuerySql, Result, sql};

#[derive(Clone, Copy)]
pub enum CrmList<'a> {
    Contacts,
    FollowUps {
        include_completed: bool,
        customer_id: Option<i64>,
        record_id: Option<i64>,
    },
    Opportunities {
        customer_id: Option<i64>,
        stage: &'a str,
        keyword: &'a str,
        /// Open opportunities with a close date, ordered for the overview.
        upcoming_closings: bool,
    },
}
pub struct CrmQuery<'a> {
    pub list: CrmList<'a>,
    pub scope: DataScope<'a>,
    pub customer_scope: DataScope<'a>,
    pub fields: &'a [(&'a str, &'a str)],
    pub offset: i64,
    pub limit: i64,
}

fn related(kind: &str, relation: &str, path: &str, json: bool, pg: bool) -> String {
    let value = if json {
        sql::json_field(path, pg)
    } else {
        sql::field(path, pg)
    }
    .replace("r.body", "related.body");
    format!(
        "(SELECT {value} FROM record_rows related WHERE related.kind='{kind}' AND related.id=CAST({} AS BIGINT))",
        sql::field(relation, pg)
    )
}
fn projected_field(list: CrmList<'_>, path: &str, json: bool, pg: bool) -> String {
    match path {
        "customerName" => related("crm-customers", "crmCustomerId", "name", json, pg),
        "contactName" if matches!(list, CrmList::FollowUps { .. }) => {
            let value = related("crm-contacts", "crmContactId", "name", json, pg);
            let empty = if !json {
                "''"
            } else if pg {
                "'\"\"'::jsonb"
            } else {
                "'\"\"'"
            };
            format!("COALESCE({value},{empty})")
        }
        "productCode" if matches!(list, CrmList::Opportunities { .. }) => {
            let value = related("products", "productId", "productCode", json, pg);
            let empty = if !json {
                "''"
            } else if pg {
                "'\"\"'::jsonb"
            } else {
                "'\"\"'"
            };
            format!("COALESCE({value},{empty})")
        }
        "linkedProductId" if matches!(list, CrmList::Opportunities { .. }) => {
            related("products", "productId", "id", json, pg)
        }
        "productName" if matches!(list, CrmList::Opportunities { .. }) => {
            let cn = related("products", "productId", "nameCN", false, pg);
            let en = related("products", "productId", "nameEN", false, pg);
            let name = format!("COALESCE(NULLIF({cn},''),{en},'')");
            if !json {
                name
            } else if pg {
                format!("to_jsonb({name})")
            } else {
                format!("json_quote({name})")
            }
        }
        _ => {
            if json {
                sql::json_field(path, pg)
            } else {
                sql::field(path, pg)
            }
        }
    }
}
pub(crate) fn build(q: &CrmQuery<'_>, pg: bool) -> Result<(QuerySql, String)> {
    let mut values = vec![];
    let (kind, order) = match q.list {
        CrmList::Contacts => ("crm-contacts", "r.id DESC"),
        CrmList::FollowUps { .. } => ("crm-follow-ups", "r.id"),
        CrmList::Opportunities {
            upcoming_closings: true,
            ..
        } => (
            "opportunities",
            if pg {
                "r.body->>'expectedCloseDate', r.id DESC"
            } else {
                "json_extract(r.body,'$.expectedCloseDate'), r.id DESC"
            },
        ),
        CrmList::Opportunities { .. } => (
            "opportunities",
            if pg {
                "CASE WHEN r.body->>'stage' IN ('已成交','已失单') THEN 1 ELSE 0 END, r.body->>'updatedAt' DESC NULLS LAST, r.id DESC"
            } else {
                "CASE WHEN json_extract(r.body,'$.stage') IN ('已成交','已失单') THEN 1 ELSE 0 END, json_extract(r.body,'$.updatedAt') DESC, r.id DESC"
            },
        ),
    };
    let parent_scope = q
        .customer_scope
        .filter(&mut values, pg)
        .replace("r.", "customer.");
    let mut clauses = vec![
        format!("r.kind='{kind}'"),
        q.scope.filter(&mut values, pg),
        format!(
            "EXISTS (SELECT 1 FROM record_rows customer WHERE customer.kind='crm-customers' AND customer.id=CAST({} AS BIGINT) AND {parent_scope})",
            sql::field("crmCustomerId", pg)
        ),
    ];
    let incomplete = if pg {
        "COALESCE((r.body->>'isCompleted')::boolean,false)=false"
    } else {
        "COALESCE(json_extract(r.body,'$.isCompleted'),0)<>1"
    };
    let customer_id = match q.list {
        CrmList::Contacts => None,
        CrmList::FollowUps {
            include_completed,
            customer_id,
            record_id,
        } => {
            if !include_completed {
                clauses.push(incomplete.into());
            }
            if let Some(id) = record_id {
                clauses.push(format!(
                    "r.id=CAST({} AS BIGINT)",
                    sql::bind(&mut values, id, pg)
                ));
            }
            customer_id
        }
        CrmList::Opportunities {
            customer_id,
            stage,
            keyword,
            upcoming_closings,
        } => {
            clauses.push(
                if pg {
                    "COALESCE((r.body->>'isArchived')::boolean,false)=false"
                } else {
                    "COALESCE(json_extract(r.body,'$.isArchived'),0)<>1"
                }
                .into(),
            );
            if !stage.is_empty() {
                clauses.push(format!(
                    "{}={}",
                    sql::field("stage", pg),
                    sql::bind(&mut values, stage, pg)
                ));
            }
            if upcoming_closings {
                clauses.push(format!(
                    "{} NOT IN ('已成交','已失单')",
                    sql::field("stage", pg)
                ));
                clauses.push(format!(
                    "COALESCE({},'')<>''",
                    sql::field("expectedCloseDate", pg)
                ));
            }
            if !keyword.is_empty() {
                let parameter = sql::bind(&mut values, keyword, pg);
                let tests = [
                    "title",
                    "quotationNo",
                    "nextAction",
                    "customerName",
                    "productCode",
                    "productName",
                ]
                .iter()
                .map(|path| {
                    sql::contains(&projected_field(q.list, path, false, pg), &parameter, pg)
                })
                .collect::<Vec<_>>();
                clauses.push(format!("({})", tests.join(" OR ")));
            }
            customer_id
        }
    };
    if let Some(id) = customer_id {
        clauses.push(format!(
            "CAST({} AS BIGINT)=CAST({} AS BIGINT)",
            sql::field("crmCustomerId", pg),
            sql::bind(&mut values, id, pg)
        ));
    }
    let order = if matches!(q.list, CrmList::FollowUps { .. }) {
        let completed = if pg {
            "COALESCE((r.body->>'isCompleted')::boolean,false)"
        } else {
            "COALESCE(json_extract(r.body,'$.isCompleted'),0)"
        };
        let next = format!("NULLIF({},'')", sql::field("nextFollowUpAt", pg));
        let followed = format!("NULLIF({},'')", sql::field("followedUpAt", pg));
        let (next_seconds, next_nanos) = sql::instant_order(&next, pg);
        let (followed_seconds, followed_nanos) = sql::instant_order(&followed, pg);
        format!(
            "{completed}, {next} IS NULL, CASE WHEN {next} IS NOT NULL THEN {next_seconds} END, CASE WHEN {next} IS NOT NULL THEN {next_nanos} END, CASE WHEN {followed} IS NOT NULL THEN {followed_seconds} END DESC NULLS LAST, CASE WHEN {followed} IS NOT NULL THEN {followed_nanos} END DESC NULLS LAST, r.id DESC"
        )
    } else {
        order.into()
    };
    let projection = sql::projected(
        q.fields.iter().map(|(name, source)| {
            sql::valid_path(source)?;
            Ok((*name, projected_field(q.list, source, true, pg)))
        }),
        pg,
    )?;
    Ok((
        QuerySql {
            filter: clauses.join(" AND "),
            order,
            values,
        },
        projection,
    ))
}
