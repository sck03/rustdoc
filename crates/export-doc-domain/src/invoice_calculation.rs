//! Decimal arithmetic shared by desktop editing and application validation.
use crate::{
    generated_api::ApiInvoiceItemDto,
    invoice::{money, number},
};
use rust_decimal::{Decimal, RoundingStrategy};

fn round(value: Decimal, digits: u32) -> Decimal {
    value.round_dp_with_strategy(digits, RoundingStrategy::MidpointAwayFromZero)
}
fn multiply(a: Decimal, b: Decimal) -> Result<Decimal, String> {
    a.checked_mul(b)
        .ok_or_else(|| "计算结果超出可表示范围。".into())
}
pub fn normalize_item(item: &mut ApiInvoiceItemDto) -> Result<(), String> {
    if item.tax_rebate_rate > Decimal::from(100) {
        return Err("退税率须在 0–100 之间。".into());
    }
    match item.price_calculation_mode.as_str() {
        "" | "UnitPriceDriven" => {
            item.price_calculation_mode = "UnitPriceDriven".into();
            item.unit_price = round(item.unit_price, 5);
            item.total_price = money(multiply(item.quantity, item.unit_price)?);
        }
        "LineAmountDriven" => {
            item.total_price = money(item.total_price);
            item.unit_price = if item.quantity > Decimal::ZERO {
                round(
                    item.total_price
                        .checked_div(item.quantity)
                        .ok_or("单价计算超出范围。")?,
                    5,
                )
            } else {
                Decimal::ZERO
            };
        }
        _ => return Err("未知计价方式，已保留原始数据。".into()),
    }
    item.gw_per_ctn = money(item.gw_per_ctn);
    item.nw_per_ctn = money(item.nw_per_ctn);
    item.gw_total = money(item.gw_total);
    item.nw_total = money(item.nw_total);
    item.volume = round(item.volume, 3);
    item.tax_refund_amount =
        item.purchase_total / Decimal::new(113, 2) * (item.tax_rebate_rate / Decimal::from(100));
    Ok(())
}
pub fn recalculate(item: &mut ApiInvoiceItemDto, changed: &[usize]) -> Result<(), String> {
    let has = |columns: &[usize]| columns.iter().any(|column| changed.contains(column));
    if has(&[24]) {
        item.price_calculation_mode = "LineAmountDriven".into();
    } else if has(&[23]) {
        item.price_calculation_mode = "UnitPriceDriven".into();
    }
    if has(&[8, 11]) && item.pcs_per_ctn > Decimal::ZERO {
        item.cartons = (item.quantity / item.pcs_per_ctn).ceil();
    }
    if has(&[8, 25]) {
        item.purchase_total = money(multiply(item.quantity, item.purchase_price)?);
    }
    if has(&[8, 11, 12, 15, 16, 17]) && !has(&[18]) {
        item.volume = multiply(
            multiply(multiply(item.length, item.width)?, item.height)?,
            item.cartons,
        )? / Decimal::from(1_000_000);
    }
    if has(&[8, 11, 12, 19]) && !has(&[20]) {
        item.gw_total = money(multiply(money(item.gw_per_ctn), item.cartons)?);
    }
    if has(&[8, 11, 12, 21]) && !has(&[22]) {
        item.nw_total = money(multiply(money(item.nw_per_ctn), item.cartons)?);
    }
    normalize_item(item)?;
    // Generated DTOs retain decimals as exact JSON numbers.
    for value in [
        item.total_price,
        item.purchase_total,
        item.gw_total,
        item.nw_total,
        item.volume,
    ] {
        if value < Decimal::ZERO {
            return Err(format!("计算结果不能为负数：{}", number(value)));
        }
    }
    Ok(())
}
