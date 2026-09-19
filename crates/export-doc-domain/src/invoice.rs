use crate::generated_api::ApiInvoiceDetailDto;
pub use crate::invoice_columns::{COLUMN_COUNT, COLUMNS, ItemRow};
use rust_decimal::{Decimal, RoundingStrategy};
use std::str::FromStr;
use unicode_normalization::UnicodeNormalization;
pub const MAX_ROWS: usize = 5_000;

#[derive(Clone, Debug, PartialEq)]
pub struct InvoiceDraft {
    pub header: ApiInvoiceDetailDto,
    pub rows: Vec<ItemRow>,
}
impl InvoiceDraft {
    pub fn new(business_date: &str) -> Self {
        Self {
            header: ApiInvoiceDetailDto {
                invoice_date: business_date.into(),
                shipment_date: business_date.into(),
                currency: "USD".into(),
                r#type: "实际数据".into(),
                status: "Draft".into(),
                trade_terms: "FOB".into(),
                transport_mode: "海运".into(),
                shipping_marks_type: "Text".into(),
                notify_party_mode: "None".into(),
                ..Default::default()
            },
            rows: vec![ItemRow::blank()],
        }
    }
    pub fn from_dto(mut invoice: ApiInvoiceDetailDto) -> Self {
        let rows = std::mem::take(&mut invoice.items)
            .into_iter()
            .map(ItemRow::from_dto)
            .collect();
        Self {
            header: invoice,
            rows,
        }
    }
    pub fn build(&self) -> Result<ApiInvoiceDetailDto, String> {
        let invoice = self.preview()?;
        if let Some((_, message)) = self.header_issue() {
            return Err(message.into());
        }
        Ok(invoice)
    }
    pub fn header_issue(&self) -> Option<(&'static str, &'static str)> {
        if normalize(&self.header.invoice_no).is_empty() {
            return Some(("invoiceNo", "请填写发票号。"));
        }
        if !valid_date(&self.header.invoice_date) {
            return Some(("invoiceDate", "发票日期请填写有效的 YYYY-MM-DD。"));
        }
        if !valid_date(&self.header.shipment_date) {
            return Some(("shipmentDate", "出运日期请填写有效的 YYYY-MM-DD。"));
        }
        None
    }
    /// Recalculate an import/editor draft before the user has filled the required
    /// header fields. Persistence still goes through build and full validation.
    pub fn preview(&self) -> Result<ApiInvoiceDetailDto, String> {
        let mut invoice = self.header.clone();
        invoice.invoice_no = normalize(&invoice.invoice_no);
        if self.rows.len() > MAX_ROWS {
            return Err(format!("商品明细最多 {MAX_ROWS} 行。"));
        }
        invoice.items = self
            .rows
            .iter()
            .enumerate()
            .filter(|(_, row)| !row.is_blank())
            .map(|(index, row)| {
                row.to_dto()
                    .map_err(|message| format!("第 {} 行 {message}", index + 1))
            })
            .collect::<Result<Vec<_>, _>>()?;
        invoice.total_amount = money(invoice.items.iter().map(|item| item.total_price).sum());
        invoice.total_quantity = money(invoice.items.iter().map(|item| item.quantity).sum());
        invoice.total_cartons = money(invoice.items.iter().map(|item| item.cartons).sum());
        invoice.total_gross_weight = invoice.items.iter().map(|item| item.gw_total).sum();
        invoice.total_net_weight = invoice.items.iter().map(|item| item.nw_total).sum();
        invoice.total_volume = invoice.items.iter().map(|item| item.volume).sum();
        invoice.total_purchase_amount =
            money(invoice.items.iter().map(|item| item.purchase_total).sum());
        invoice.total_tax_refund_amount = money(
            invoice
                .items
                .iter()
                .map(|item| item.tax_refund_amount)
                .sum(),
        );
        let rate = invoice.exchange_rate.or_else(|| {
            if invoice.currency.eq_ignore_ascii_case("CNY") {
                Some(Decimal::ONE)
            } else {
                None
            }
        });
        invoice.total_profit = if let Some(rate) = rate.filter(|rate| *rate > Decimal::ZERO) {
            money(
                invoice
                    .total_amount
                    .checked_mul(rate)
                    .and_then(|amount| amount.checked_sub(invoice.total_purchase_amount))
                    .and_then(|profit| profit.checked_add(invoice.total_tax_refund_amount))
                    .ok_or("利润计算超出范围。")?,
            )
        } else {
            Decimal::ZERO
        };
        Ok(invoice)
    }
    pub fn demo(business_date: &str, invoice_no: &str) -> Self {
        let mut draft = Self::new(business_date);
        draft.header.invoice_no = invoice_no.into();
        draft.header.contract_no = "NATIVE-2026-001".into();
        draft.header.exporter_name_en = "BRIDGE TEXTILE CO., LTD.".into();
        draft.header.exporter_name_cn = "桥联纺织有限公司（验证样例）".into();
        draft.header.exporter_address_en = "88 Textile Road, Ningbo, China".into();
        draft.header.customer_name_en = "NORTHSHORE TRADING LTD.".into();
        draft.header.customer_address_en = "28 River Street, London, United Kingdom".into();
        draft.header.port_of_loading = "NINGBO".into();
        draft.header.port_of_destination = "LONDON".into();
        draft.header.destination_country = "UNITED KINGDOM".into();
        draft.header.payment_terms = "T/T 30 DAYS".into();
        draft.header.shipping_marks = "NORTHSHORE\nLONDON\nMADE IN CHINA".into();
        draft.rows.clear();
        for (po, style, en, cn, quantity, price) in [
            (
                "PO-260901",
                "TS-001",
                "MEN'S COTTON T-SHIRT",
                "男式纯棉针织T恤",
                "1000",
                "4.50",
            ),
            (
                "PO-260902",
                "WS-002",
                "WOMEN'S WOVEN SHIRT",
                "女式梭织衬衫",
                "600",
                "8.75",
            ),
            (
                "PO-260903",
                "JB-003",
                "COTTON CANVAS BAG",
                "纯棉帆布购物袋",
                "400",
                "2.35",
            ),
        ] {
            let mut row = ItemRow::blank();
            for (column, value) in [
                (0, po),
                (1, style),
                (2, en),
                (3, cn),
                (4, "100% COTTON"),
                (5, "BRIDGE"),
                (6, "6109100000"),
                (8, quantity),
                (11, "50"),
                (15, "50"),
                (16, "40"),
                (17, "30"),
                (19, "12.5"),
                (21, "11.5"),
                (23, price),
            ] {
                row.cells[column] = value.into();
            }
            row.recalculate(&[8, 11, 15, 16, 17, 19, 21, 23])
                .expect("valid synthetic fixture");
            draft.rows.push(row);
        }
        draft
    }
    /// Validate the complete paste before changing anything, including existing
    /// hidden spare fields and server-owned identifiers on the target rows.
    pub fn paste(
        &mut self,
        start_row: usize,
        start_column: usize,
        text: &str,
    ) -> Result<usize, String> {
        if text.len() > 4 * 1024 * 1024 {
            return Err("粘贴内容超过 4 MiB。".into());
        }
        let grid = parse_tsv(text)?;
        if grid.is_empty() {
            return Ok(0);
        }
        if start_row > self.rows.len() || start_row + grid.len() > MAX_ROWS {
            return Err(format!("表格最多 {MAX_ROWS} 行。"));
        }
        let mut candidate = self.rows.clone();
        for (offset, cells) in grid.iter().enumerate() {
            if start_column + cells.len() > COLUMN_COUNT {
                return Err("粘贴列超出了商品表格范围。".into());
            }
            while candidate.len() <= start_row + offset {
                candidate.push(ItemRow::blank());
            }
            let row = &mut candidate[start_row + offset];
            for (column, text) in cells.iter().enumerate() {
                row.cells[start_column + column] = normalize(text);
            }
            let changed: Vec<_> = (start_column..start_column + cells.len()).collect();
            row.recalculate(&changed)
                .map_err(|message| format!("粘贴第 {} 行：{message}", offset + 1))?;
            row.to_dto()
                .map_err(|message| format!("粘贴第 {} 行：{message}", offset + 1))?;
        }
        self.rows = candidate;
        Ok(grid.len())
    }
}

pub fn number(value: Decimal) -> String {
    value.normalize().to_string()
}
pub fn money(value: Decimal) -> Decimal {
    value.round_dp_with_strategy(2, RoundingStrategy::MidpointAwayFromZero)
}
pub fn normalize(value: &str) -> String {
    value.trim().nfc().collect()
}
pub fn parse_number(value: &str) -> Result<Decimal, String> {
    let value = value.trim();
    if value.is_empty() {
        return Ok(Decimal::ZERO);
    }
    let parsed = Decimal::from_str(value)
        .map_err(|_| "请输入数字，使用小数点，不含千位分隔符。".to_owned())?;
    if parsed < Decimal::ZERO || parsed > Decimal::from(1_000_000_000u64) || parsed.scale() > 12 {
        return Err("数字范围应为 0–10 亿，最多 12 位小数。".into());
    }
    Ok(parsed)
}

pub fn valid_date(value: &str) -> bool {
    let parts: Vec<_> = value.split('-').collect();
    if parts.len() != 3 || parts[0].len() != 4 || parts[1].len() != 2 || parts[2].len() != 2 {
        return false;
    }
    let (Ok(year), Ok(month), Ok(day)) = (
        parts[0].parse::<u32>(),
        parts[1].parse::<u32>(),
        parts[2].parse::<u32>(),
    ) else {
        return false;
    };
    if year == 0 || !(1..=12).contains(&month) {
        return false;
    }
    let leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
    let days = [
        31,
        if leap { 29 } else { 28 },
        31,
        30,
        31,
        30,
        31,
        31,
        30,
        31,
        30,
        31,
    ];
    day > 0 && day <= days[month as usize - 1]
}

pub fn parse_tsv(text: &str) -> Result<Vec<Vec<String>>, String> {
    let mut rows = vec![];
    let mut row = vec![];
    let mut cell = String::new();
    let mut quoted = false;
    let mut chars = text.chars().peekable();
    while let Some(character) = chars.next() {
        match character {
            '"' if quoted && chars.peek() == Some(&'"') => {
                cell.push('"');
                chars.next();
            }
            '"' if quoted => quoted = false,
            '"' if cell.is_empty() => quoted = true,
            '\t' if !quoted => row.push(std::mem::take(&mut cell)),
            '\n' if !quoted => {
                row.push(std::mem::take(&mut cell));
                rows.push(std::mem::take(&mut row));
            }
            '\r' if !quoted && chars.peek() == Some(&'\n') => {}
            other => cell.push(other),
        }
    }
    if quoted {
        return Err("粘贴内容包含未闭合的引号。".into());
    }
    if !cell.is_empty() || !row.is_empty() {
        row.push(cell);
        rows.push(row);
    }
    Ok(rows)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn exact_decimal_roundtrip_and_rounding_match_invoice_boundary() {
        assert_eq!(
            money(Decimal::from_str("1.005").unwrap()).to_string(),
            "1.01"
        );
        let mut draft = InvoiceDraft::demo("2026-09-16", "TEST-001");
        draft.rows[0].cells[23] = "0.123456789012".into();
        draft.rows[0].edit_finished(23);
        let invoice = draft.build().unwrap();
        let roundtrip: ApiInvoiceDetailDto =
            serde_json::from_str(&serde_json::to_string(&invoice).unwrap()).unwrap();
        assert_eq!(invoice.items[0].unit_price, roundtrip.items[0].unit_price);
    }
    #[test]
    fn paste_is_atomic_and_preserves_hidden_fields() {
        let mut draft = InvoiceDraft::demo("2026-09-16", "TEST");
        draft.rows[0].cells[37] = "保留备用十".into();
        let before = draft.clone();
        assert!(draft.paste(0, 8, "NaN\tPCS").is_err());
        assert_eq!(before, draft);
        draft.paste(0, 8, "1200").unwrap();
        draft.paste(0, 23, "4.55").unwrap();
        assert_eq!(draft.rows[0].cells[37], "保留备用十");
        assert_eq!(draft.rows[0].cells[24], "5460");
    }
    #[test]
    fn quoted_tsv_and_calendar_dates() {
        assert_eq!(
            parse_tsv("\"a\nb\"\t\"x\"\"y\"\r\n").unwrap(),
            vec![vec!["a\nb", "x\"y"]]
        );
        assert!(valid_date("2024-02-29"));
        assert!(!valid_date("2026-02-29"));
        assert!(!valid_date("2026-09-31"));
    }
}
