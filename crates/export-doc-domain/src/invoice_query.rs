//! The query page filters shipment dates and searches business fields only.
use crate::generated_api::{ApiInvoiceDetailDto, ApiQueryInvoiceFilterRequest};
use unicode_normalization::UnicodeNormalization;

pub const COLUMNS: &[(&str, &str)] = &[
    ("invoiceNo", "发票号"),
    ("invoiceDate", "日期"),
    ("contractNo", "合同号"),
    ("customerName", "客户"),
    ("exporterName", "出口商"),
    ("destinationCountry", "目的国"),
    ("tradeTerms", "贸易条款"),
    ("shipmentDate", "船期/航期"),
    ("transportMode", "运输方式"),
    ("totalCartons", "总箱数"),
    ("totalQuantity", "总数量"),
    ("totalAmount", "总金额"),
    ("currency", "币种"),
    ("type", "类型"),
];
fn normalized(value: &str) -> String {
    value.nfc().collect::<String>().trim().to_lowercase()
}
#[derive(PartialEq, Eq, PartialOrd, Ord)]
struct CalendarDate(String);
fn date(value: Option<&str>) -> Result<Option<CalendarDate>, String> {
    value
        .filter(|v| !v.is_empty())
        .map(|v| {
            if crate::invoice::valid_date(v) {
                Ok(CalendarDate(v.into()))
            } else {
                Err("查询日期须为 YYYY-MM-DD。".into())
            }
        })
        .transpose()
}
fn optional(value: &Option<String>) -> &str {
    value.as_deref().unwrap_or("")
}
pub struct Criteria {
    filter: ApiQueryInvoiceFilterRequest,
    start: Option<CalendarDate>,
    end: Option<CalendarDate>,
    tokens: Vec<String>,
}
impl Criteria {
    pub fn new(mut filter: ApiQueryInvoiceFilterRequest) -> Result<Self, String> {
        let start = date(filter.start_date.as_deref())?;
        let end = date(filter.end_date_exclusive.as_deref())?;
        if start
            .as_ref()
            .zip(end.as_ref())
            .is_some_and(|(s, e)| s >= e)
        {
            return Err("结束日期须晚于开始日期。".into());
        }
        for field in [
            &mut filter.keyword,
            &mut filter.contract_no,
            &mut filter.invoice_type,
            &mut filter.transport_mode,
            &mut filter.style_name,
            &mut filter.style_no,
        ] {
            if optional(field).chars().count() > 250 {
                return Err("查询条件不能超过 250 字。".into());
            }
            *field = Some(normalized(optional(field)));
        }
        if !["", "实际数据", "报关数据"].contains(&optional(&filter.invoice_type)) {
            return Err("单据类型只能为实际数据或报关数据。".into());
        }
        if filter.customer_id.is_some_and(|id| id < 0)
            || filter.exporter_id.is_some_and(|id| id < 0)
        {
            return Err("往来单位编号不能为负数。".into());
        }
        let tokens = optional(&filter.keyword)
            .split_whitespace()
            .map(str::to_owned)
            .collect();
        Ok(Self {
            filter,
            start,
            end,
            tokens,
        })
    }
    pub fn matches(&self, invoice: &ApiInvoiceDetailDto) -> Result<bool, String> {
        let f = &self.filter;
        if self.start.is_some() || self.end.is_some() {
            let shipment =
                date(Some(&invoice.shipment_date))?.ok_or("已保存单据缺少船期/航期。")?;
            if self.start.as_ref().is_some_and(|s| &shipment < s)
                || self.end.as_ref().is_some_and(|e| &shipment >= e)
            {
                return Ok(false);
            }
        }
        let includes = |value: &str, term: &str| normalized(value).contains(term);
        if f.customer_id
            .filter(|id| *id > 0)
            .is_some_and(|id| invoice.customer_id != id)
            || f.exporter_id
                .filter(|id| *id > 0)
                .is_some_and(|id| invoice.exporter_id != id)
            || (!optional(&f.invoice_type).is_empty()
                && normalized(&invoice.r#type) != optional(&f.invoice_type))
            || (!optional(&f.transport_mode).is_empty()
                && normalized(&invoice.transport_mode) != optional(&f.transport_mode))
            || (!optional(&f.contract_no).is_empty()
                && f.contract_no != f.keyword
                && !includes(&invoice.contract_no, optional(&f.contract_no)))
            || (!optional(&f.style_name).is_empty()
                && !invoice
                    .items
                    .iter()
                    .any(|i| includes(&i.style_name, optional(&f.style_name))))
            || (!optional(&f.style_no).is_empty()
                && !invoice
                    .items
                    .iter()
                    .any(|i| includes(&i.style_no, optional(&f.style_no))))
        {
            return Ok(false);
        }
        Ok(self.tokens.iter().all(|token| {
            let identifier = token.chars().any(|c| c.is_numeric());
            let matched = |s: &str| {
                if identifier {
                    normalized(s).starts_with(token)
                } else {
                    includes(s, token)
                }
            };
            matched(&invoice.invoice_no)
                || matched(&invoice.contract_no)
                || invoice
                    .items
                    .iter()
                    .any(|i| matched(&i.po_number) || matched(&i.style_no) || matched(&i.hs_code))
                || (!identifier
                    && ([
                        &invoice.customer_name_en,
                        &invoice.notify_party_name,
                        &invoice.exporter_name_en,
                        &invoice.exporter_name_cn,
                        &invoice.destination_country,
                        &invoice.port_of_loading,
                        &invoice.port_of_destination,
                        &invoice.trade_terms,
                        &invoice.transport_mode,
                    ]
                    .iter()
                    .any(|s| matched(s))
                        || invoice.items.iter().any(|i| {
                            [&i.style_name, &i.style_name_cn, &i.brand, &i.origin]
                                .iter()
                                .any(|s| matched(s))
                        })))
        }))
    }
}
