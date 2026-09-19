//! Editable columns follow the React invoice contract, in the same order.
use crate::generated_api::ApiInvoiceItemDto;
use crate::invoice::{normalize, number, parse_number};
use rust_decimal::Decimal;

#[derive(Clone, Copy, Debug)]
pub struct Column {
    pub key: &'static str,
    pub label: &'static str,
    pub width: u16,
    pub numeric: bool,
}
macro_rules! is_numeric {
    (text) => {
        false
    };
    (number) => {
        true
    };
}
macro_rules! cell_text {
    (text, $value:expr) => {
        $value.clone()
    };
    (number, $value:expr) => {
        number($value)
    };
}
macro_rules! cell_value {
    (text, $value:expr) => {
        normalize($value)
    };
    (number, $value:expr) => {
        parse_number($value)?
    };
}
macro_rules! columns {
    ($(($field:ident, $key:literal, $label:literal, $width:literal, $kind:ident)),+ $(,)?) => {
        pub const COLUMN_COUNT: usize = 38;
        pub const COLUMNS: [&str; COLUMN_COUNT] = [$($label),+];
        pub const ITEM_COLUMNS: [Column; COLUMN_COUNT] = [
            $(Column {key:$key, label:$label, width:$width, numeric:is_numeric!($kind)}),+
        ];
        pub fn cells(item: &ApiInvoiceItemDto) -> [String; COLUMN_COUNT] {
            [$(cell_text!($kind, item.$field)),+]
        }
        pub fn assign(item: &mut ApiInvoiceItemDto, cells: &[String; COLUMN_COUNT]) -> Result<(), String> {
            let mut values = cells.iter();
            $(item.$field = cell_value!($kind, values.next().expect("fixed column count"));)+
            Ok(())
        }
    };
}
columns![
    (po_number, "poNumber", "PO", 100, text),
    (style_no, "styleNo", "款号", 100, text),
    (style_name, "styleName", "英文品名", 172, text),
    (style_name_cn, "styleNameCN", "中文品名", 172, text),
    (fabric_composition, "fabricComposition", "成分", 156, text),
    (brand, "brand", "品牌", 100, text),
    (hs_code, "hsCode", "HS 编码", 112, text),
    (origin, "origin", "原产地", 100, text),
    (quantity, "quantity", "数量", 100, number),
    (unit_en, "unitEN", "单位 EN", 94, text),
    (unit_cn, "unitCN", "单位 CN", 94, text),
    (pcs_per_ctn, "pcsPerCtn", "每箱", 94, number),
    (cartons, "cartons", "箱数", 94, number),
    (ctn_unit_en, "ctnUnitEN", "箱单位", 100, text),
    (ctn_unit_cn, "ctnUnitCN", "箱单位 CN", 104, text),
    (length, "length", "长", 84, number),
    (width, "width", "宽", 84, number),
    (height, "height", "高", 84, number),
    (volume, "volume", "体积", 100, number),
    (gw_per_ctn, "gwPerCtn", "毛重/箱", 100, number),
    (gw_total, "gwTotal", "总毛重", 100, number),
    (nw_per_ctn, "nwPerCtn", "净重/箱", 100, number),
    (nw_total, "nwTotal", "总净重", 100, number),
    (unit_price, "unitPrice", "单价", 108, number),
    (total_price, "totalPrice", "金额", 116, number),
    (purchase_price, "purchasePrice", "采购价", 100, number),
    (purchase_total, "purchaseTotal", "采购额", 116, number),
    (tax_rebate_rate, "taxRebateRate", "退税率", 94, number),
    (spare1, "spare1", "备用 1", 100, text),
    (spare2, "spare2", "备用 2", 100, text),
    (spare3, "spare3", "备用 3", 100, text),
    (spare4, "spare4", "备用 4", 100, text),
    (spare5, "spare5", "备用 5", 100, text),
    (spare6, "spare6", "备用 6", 100, text),
    (spare7, "spare7", "备用 7", 100, text),
    (spare8, "spare8", "备用 8", 100, text),
    (spare9, "spare9", "备用 9", 100, text),
    (spare10, "spare10", "备用 10", 100, text),
];
pub fn column_index(key: &str) -> Option<usize> {
    ITEM_COLUMNS.iter().position(|column| column.key == key)
}

#[derive(Clone, Debug, PartialEq)]
pub struct ItemRow {
    pub original: ApiInvoiceItemDto,
    pub cells: [String; COLUMN_COUNT],
}
impl ItemRow {
    pub fn blank() -> Self {
        Self::from_dto(ApiInvoiceItemDto {
            unit_en: "PCS".into(),
            unit_cn: "件".into(),
            ctn_unit_en: "CTNS".into(),
            ctn_unit_cn: "箱".into(),
            origin: "CHINA".into(),
            price_calculation_mode: "UnitPriceDriven".into(),
            ..Default::default()
        })
    }
    pub fn from_dto(item: ApiInvoiceItemDto) -> Self {
        Self {
            cells: cells(&item),
            original: item,
        }
    }
    pub fn is_blank(&self) -> bool {
        self.cells.iter().enumerate().all(|(i, value)| {
            matches!(i, 7 | 9 | 10 | 13 | 14)
                || value.trim().is_empty()
                || (ITEM_COLUMNS[i].numeric && parse_number(value) == Ok(Decimal::ZERO))
        })
    }
    pub fn to_dto(&self) -> Result<ApiInvoiceItemDto, String> {
        let mut item = self.original.clone();
        for (index, value) in self.cells.iter().enumerate() {
            if value.chars().count() > 500 {
                return Err(format!("{}最多 500 字。", COLUMNS[index]));
            }
            if ITEM_COLUMNS[index].numeric {
                parse_number(value).map_err(|error| format!("{}：{error}", COLUMNS[index]))?;
            }
        }
        assign(&mut item, &self.cells)?;
        crate::invoice_calculation::normalize_item(&mut item)?;
        Ok(item)
    }
    /// Invalid intermediate typing remains in the editor; a commit reports it.
    pub fn edit_finished(&mut self, column: usize) {
        let _ = self.recalculate(&[column]);
    }
    pub fn recalculate(&mut self, changed: &[usize]) -> Result<(), String> {
        let mut item = self.original.clone();
        assign(&mut item, &self.cells)?;
        crate::invoice_calculation::recalculate(&mut item, changed)?;
        *self = Self::from_dto(item);
        Ok(())
    }
}
