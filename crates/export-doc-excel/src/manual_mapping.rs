//! Explicit per-import coordinates; never consulted by automatic detection or exports.
use crate::{Result, mapping};
use export_doc_domain::invoice_columns::ITEM_COLUMNS;
use exportdoc_excel_analyzer::{FieldCandidate, TableAnalysis};
use serde_json::Value;
use std::collections::BTreeMap;

pub(crate) struct ManualMapping {
    pub sheet: String,
    pub start: usize,
    pub end: usize,
    pub columns: BTreeMap<usize, usize>,
    pub headers: Vec<(&'static str, u32, u32)>,
    pub multiline: Vec<(&'static str, u32, u32, usize)>,
}
impl ManualMapping {
    pub fn read(value: &Value) -> Result<Self> {
        let number = |key: &str, maximum: usize| -> Result<usize> {
            value[key]
                .as_u64()
                .or_else(|| value[key].as_str()?.parse().ok())
                .filter(|n| *n <= maximum as u64)
                .map(|n| n as usize)
                .ok_or_else(|| format!("手动方案 {key} 必须是 0–{maximum} 的整数。"))
        };
        let sheet = value["sheetName"].as_str().unwrap_or("").trim().to_owned();
        if sheet.is_empty() {
            return Err("手动方案必须填写工作表名称。".into());
        }
        let start = number("itemsStartRow", 10000)?;
        let end = number("itemsEndRow", 10000)?;
        if start == 0 || end != 0 && end < start {
            return Err("手动方案明细行范围无效。".into());
        }
        let mut columns = BTreeMap::new();
        for (index, column) in ITEM_COLUMNS.iter().enumerate() {
            let key = format!("{}Col", column.key);
            if value.get(&key).is_some() {
                let col = number(&key, 128)?;
                if col > 0 {
                    columns.insert(index, col);
                }
            }
        }
        if !columns.contains_key(&8) || ![1, 2, 3].iter().any(|i| columns.contains_key(i)) {
            return Err("手动方案至少映射数量，以及款号或品名；不用的列填 0。".into());
        }
        let mut headers = Vec::new();
        for &(field, setting) in HEADER_SETTINGS {
            let address = value[setting].as_str().unwrap_or("");
            if address.is_empty() {
                continue;
            }
            let (row, col) = checked_cell(address)?;
            headers.push((field, row, col));
        }
        let mut multiline = Vec::new();
        for (field, prefix) in [
            ("exporterAddressEN", "exporterAddress"),
            ("customerAddressEN", "customerAddress"),
            ("notifyPartyAddress", "notifyPartyAddress"),
        ] {
            let address = value[format!("{prefix}StartCell")].as_str().unwrap_or("");
            if address.is_empty() {
                continue;
            }
            let (row, col) = checked_cell(address)?;
            multiline.push((field, row, col, number(&format!("{prefix}LineCount"), 20)?));
        }
        Ok(Self {
            sheet,
            start,
            end,
            columns,
            headers,
            multiline,
        })
    }
    pub fn table(&self) -> TableAnalysis {
        TableAnalysis {
            header_start_row: self.start.saturating_sub(1),
            header_depth: usize::from(self.start > 1),
            data_start_row: self.start,
            confidence: 1.0,
            fields: self
                .columns
                .iter()
                .map(|(&index, &column)| FieldCandidate {
                    canonical_field: ITEM_COLUMNS[index].key.into(),
                    column,
                    header_path: vec![],
                    confidence: 1.0,
                })
                .collect(),
            sample_rows: vec![],
        }
    }
}
fn checked_cell(address: &str) -> Result<(u32, u32)> {
    mapping::cell(address)
        .filter(|(row, col)| *row <= 10000 && *col <= 128)
        .ok_or_else(|| format!("手动方案单元格 {address} 无效，须在 10000 行、128 列以内。"))
}
const HEADER_SETTINGS: &[(&str, &str)] = &[
    ("exporterNameCN", "exporterNameCNCell"),
    ("exporterNameEN", "exporterNameCell"),
    ("exporterCreditCode", "creditCodeCell"),
    ("customerNameEN", "customerNameCell"),
    ("notifyPartyName", "notifyPartyNameCell"),
    ("invoiceDate", "invoiceDateCell"),
    ("contractNo", "contractNoCell"),
    ("issuingBank", "issuingBankCell"),
    ("currency", "currencyCell"),
    ("invoiceNo", "invoiceNoCell"),
    ("supervisionMode", "supervisionModeCell"),
    ("letterOfCreditNo", "letterOfCreditNoCell"),
    ("paymentTerms", "paymentTermsCell"),
    ("transportMode", "transportModeCell"),
    ("tradeTerms", "tradeTermsCell"),
    ("portOfLoading", "portOfLoadingCell"),
    ("portOfDestination", "portOfDestinationCell"),
    ("destinationCountry", "destinationCountryCell"),
    ("shippingMarks", "shippingMarksCell"),
];
