use super::{
    auth,
    error::{Result, unavailable},
    records::text,
    store::{Actor, Store},
};
use crate::{clock::BusinessClock, contracts, generated_api::*};
use chrono::{Datelike, Months, NaiveDate};
use rust_decimal::Decimal;
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub fn query(store: &Store, actor: &Actor, clock: &BusinessClock) -> Result<Value> {
    auth::authorize(actor, "document.invoices", "view")?;
    let today = clock.now().map_err(unavailable)?.today;
    let start = today
        .with_day(1)
        .ok_or_else(|| unavailable("业务月份无效。"))?;
    let previous = start
        .checked_sub_months(Months::new(1))
        .ok_or_else(|| unavailable("业务月份超出范围。"))?;
    let end = start
        .checked_add_months(Months::new(1))
        .ok_or_else(|| unavailable("业务月份超出范围。"))?;
    let mut totals = [[Decimal::ZERO; 2]; 2];
    let mut monthly_count = 0usize;
    let mut currencies = BTreeMap::<String, [Decimal; 2]>::new();
    // Historical rows contribute only to database counts; monetary scans cover two months.
    let mut todo_records = BTreeMap::<String, Vec<Value>>::new();
    let schema = contracts::schema("ApiDashboardRecentInvoiceDto");
    let mut fields: Vec<_> = schema["properties"]
        .as_object()
        .into_iter()
        .flat_map(|p| p.keys())
        .map(|key| {
            let source = match key.as_str() {
                "customerName" => "customerNameEN",
                "exporterName" => "exporterNameEN",
                key => key,
            };
            (key.as_str(), source)
        })
        .collect();
    for key in ["id", "invoiceNo", "status"] {
        if !fields.iter().any(|(name, _)| *name == key) {
            fields.push((key, key));
        }
    }
    let (counts, records) = store.read(|tx| {
        let actor = auth::refresh_actor_in(tx, actor)?;
        auth::authorize(&actor, "document.invoices", "view")?;
        let mut before = None;
        let scope = auth::data_scope(&actor, "document.invoices", "view");
        let counts = tx.dashboard_counts(&scope)?;
        let records = tx.scan_dashboard(&scope, &fields, None, None, "", 10)?;
        for (status, limit) in [("Draft", 3), ("Verified", 5), ("Shipped", 5)] {
            todo_records.insert(
                status.into(),
                tx.scan_dashboard(
                    &scope,
                    &[("id", "id"), ("invoiceNo", "invoiceNo")],
                    None,
                    None,
                    status,
                    limit,
                )?,
            );
        }
        let window_start = previous.to_string();
        let window_end = end.to_string();
        let amount_fields = [
            "id",
            "invoiceDate",
            "currency",
            "totalAmount",
            "totalProfit",
            "totalTaxRefundAmount",
        ]
        .map(|field| (field, field));
        loop {
            crate::operation::check()?;
            let batch = tx.scan_dashboard(
                &scope,
                &amount_fields,
                before,
                Some((&window_start, &window_end)),
                "",
                200,
            )?;
            if batch.is_empty() {
                break;
            }
            for record in batch {
                crate::operation::check()?;
                before = Some(
                    record["id"]
                        .as_i64()
                        .ok_or_else(|| unavailable("发票编号损坏。"))?,
                );
                let date = NaiveDate::parse_from_str(&text(&record, "invoiceDate"), "%Y-%m-%d")
                    .map_err(|_| unavailable("发票业务日期损坏。"))?;
                let period = usize::from(date >= start);
                monthly_count += period;
                let currency = text(&record, "currency").trim().to_ascii_uppercase();
                if currency.is_empty() {
                    return Err(unavailable("发票币种缺失。"));
                }
                let amount: Decimal = serde_json::from_value(record["totalAmount"].clone())
                    .map_err(|_| unavailable("发票出口金额无效。"))?;
                let amounts = currencies.entry(currency).or_default();
                amounts[period] = amounts[period]
                    .checked_add(amount)
                    .ok_or_else(|| unavailable("币种金额汇总超出范围。"))?;
                for (column, key) in ["totalProfit", "totalTaxRefundAmount"].iter().enumerate() {
                    let value: Decimal = serde_json::from_value(record[*key].clone())
                        .map_err(|_| unavailable(format!("发票统计字段 {key} 无效。")))?;
                    totals[period][column] = totals[period][column]
                        .checked_add(value)
                        .ok_or_else(|| unavailable("发票汇总超出金额范围。"))?;
                }
            }
        }
        Ok((counts, records))
    })?;
    let mut result = contracts::object(GET_DASHBOARD.id, false);
    for (period, prefix) in [(0, "previousMonthly"), (1, "monthly")] {
        for (column, suffix) in ["Profit", "TaxRefund"].iter().enumerate() {
            result[format!("{prefix}{suffix}")] = json!(totals[period][column]);
        }
    }
    result["monthlyInvoiceCount"] = json!(monthly_count);
    result["exportAmountsByCurrency"] = json!(currencies.into_iter().map(|(currency,amounts)|json!({"currency":currency,"monthlyAmount":amounts[1],"previousMonthlyAmount":amounts[0]})).collect::<Vec<_>>());
    result["periodLabel"] = json!(format!("{}年{}月", today.year(), today.month()));
    for (field, count) in [
        ("draftCount", counts.draft),
        ("verifiedCount", counts.verified),
        ("shippedCount", counts.shipped),
        ("completedCount", counts.completed),
    ] {
        result[field] = json!(count);
    }
    result["pendingCount"] = json!(
        result["draftCount"].as_u64().unwrap_or(0) + result["verifiedCount"].as_u64().unwrap_or(0)
    );
    result["totalActiveCount"] = json!(counts.total);
    result["recentInvoices"] = json!(
        records
            .iter()
            .take(10)
            .map(|record| {
                let mut row = record.clone();
                row["statusText"] = json!(match text(record, "status").as_str() {
                    "Draft" => "草稿",
                    "Verified" => "已核对",
                    "Shipped" => "已出运",
                    "Completed" => "已结汇",
                    _ => "",
                });
                row
            })
            .collect::<Vec<_>>()
    );
    let mut todos = vec![];
    for (status, title, description, count) in [
        ("Shipped", "待收款 (Unpaid)", "已出运，等待结汇。", 5),
        (
            "Verified",
            "待出运 (Pending Shipment)",
            "已核对，等待安排出运。",
            5,
        ),
        (
            "Draft",
            "待核对 (Pending Verification)",
            "仍在草稿状态。",
            3,
        ),
    ] {
        todos.extend(todo_records.get(status).into_iter().flatten().take(count).map(|record|json!({"title":title,"description":format!("发票 {} {description}",text(record,"invoiceNo")),"actionType":"ViewInvoice","referenceId":record["id"].to_string()})));
    }
    result["todoItems"] = json!(todos);
    result["storagePolicy"] = json!(
        "仅汇总当前账号有权查看的单据。同一公司、发票号优先使用实际数据，再使用报关数据。出口额按原币分别展示，本月与上月只比较同一币种；利润与退税额为人民币。付款与报销不计入出口金额。"
    );
    result["singleWindowStatusSummary"] = json!("单一窗口近况：请在单一窗口中查看申报批次及回执。");
    Ok(result)
}
