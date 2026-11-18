//! Profit uses the same rounded invoice totals for editing, persistence and analysis.
use crate::{generated_api::ApiInvoiceDetailDto, invoice::money};
use rust_decimal::Decimal;

pub struct Profit {
    pub exchange_rate: Option<Decimal>,
    pub sales_rmb: Decimal,
    pub gross_profit: Decimal,
    pub margin: Decimal,
}

pub fn calculate(invoice: &ApiInvoiceDetailDto) -> Result<Profit, String> {
    let exchange_rate = invoice
        .exchange_rate
        .filter(|rate| *rate > Decimal::ZERO)
        .or_else(|| {
            let currency = invoice.currency.trim();
            (currency.eq_ignore_ascii_case("CNY") || currency.eq_ignore_ascii_case("RMB"))
                .then_some(Decimal::ONE)
        });
    let sales_rmb = money(invoice.total_amount)
        .checked_mul(exchange_rate.unwrap_or_default())
        .ok_or("利润计算超出范围。")?;
    let gross_profit = if exchange_rate.is_some() {
        money(
            sales_rmb
                .checked_sub(money(invoice.total_purchase_amount))
                .and_then(|value| value.checked_add(money(invoice.total_tax_refund_amount)))
                .ok_or("利润计算超出范围。")?,
        )
    } else {
        Decimal::ZERO
    };
    let margin = if sales_rmb > Decimal::ZERO {
        gross_profit
            .checked_div(sales_rmb)
            .ok_or("利润率计算超出范围。")?
    } else {
        Decimal::ZERO
    };
    Ok(Profit {
        exchange_rate,
        sales_rmb,
        gross_profit,
        margin,
    })
}
