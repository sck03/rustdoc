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
    let mut preferred = BTreeMap::<(String, String), Value>::new();
    for record in store.all("invoices")?.into_iter().filter(|record| {
        record["status"] != "Cancelled" && auth::visible(actor, "document.invoices", "view", record)
    }) {
        crate::operation::check()?;
        let key = (text(&record, "companyScope"), text(&record, "invoiceNo"));
        let rank = |record: &Value| {
            let kind = text(record, "type");
            (
                if kind.contains("实际") {
                    2
                } else if kind.contains("报关") {
                    1
                } else {
                    0
                },
                record["id"].as_i64().unwrap_or(0),
            )
        };
        if preferred
            .get(&key)
            .is_none_or(|current| rank(&record) > rank(current))
        {
            preferred.insert(key, record);
        }
    }
    let mut records: Vec<_> = preferred.into_values().collect();
    records.sort_by_key(|record| std::cmp::Reverse(record["id"].as_i64()));
    let mut totals = [[Decimal::ZERO; 3]; 2];
    let mut counts = [0usize; 2];
    for record in &records {
        let date = NaiveDate::parse_from_str(&text(record, "invoiceDate"), "%Y-%m-%d")
            .map_err(|_| unavailable("发票业务日期损坏。"))?;
        if date < previous || date >= end {
            continue;
        }
        let period = usize::from(date >= start);
        counts[period] += 1;
        for (column, key) in ["totalAmount", "totalProfit", "totalTaxRefundAmount"]
            .iter()
            .enumerate()
        {
            let value: Decimal = serde_json::from_value(record[*key].clone())
                .map_err(|_| unavailable(format!("发票统计字段 {key} 无效。")))?;
            totals[period][column] = totals[period][column]
                .checked_add(value)
                .ok_or_else(|| unavailable("发票汇总超出金额范围。"))?;
        }
    }
    let mut result = contracts::object(GET_DASHBOARD.id, false);
    for (period, prefix) in [(0, "previousMonthly"), (1, "monthly")] {
        for (column, suffix) in ["ExportAmount", "Profit", "TaxRefund"].iter().enumerate() {
            result[format!("{prefix}{suffix}")] = json!(totals[period][column]);
        }
    }
    result["monthlyInvoiceCount"] = json!(counts[1]);
    result["periodLabel"] = json!(format!("{}年{}月", today.year(), today.month()));
    for (field, status) in [
        ("draftCount", "Draft"),
        ("verifiedCount", "Verified"),
        ("shippedCount", "Shipped"),
        ("completedCount", "Completed"),
    ] {
        result[field] = json!(
            records
                .iter()
                .filter(|record| record["status"] == status)
                .count()
        );
    }
    result["pendingCount"] = json!(
        result["draftCount"].as_u64().unwrap_or(0) + result["verifiedCount"].as_u64().unwrap_or(0)
    );
    result["totalActiveCount"] = json!(records.len());
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
        todos.extend(records.iter().filter(|record|record["status"]==status).take(count).map(|record|json!({"title":title,"description":format!("发票 {} {description}",text(record,"invoiceNo")),"actionType":"ViewInvoice","referenceId":record["id"].to_string()})));
    }
    result["todoItems"] = json!(todos);
    result["storagePolicy"] = json!(
        "仅汇总当前账号有权查看的单据。同一公司、发票号优先使用实际数据，再使用报关数据。付款与报销不计入出口金额。"
    );
    result["singleWindowStatusSummary"] = json!("单一窗口近况：请在单一窗口中查看申报批次及回执。");
    Ok(result)
}
