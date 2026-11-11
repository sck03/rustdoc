//! The shipped COO workbook has an explicit layout; customer files still use the shared analyzer.
use crate::{Check, Result, import};
use calamine::{Data, Range, Reader, Sheets};
use export_doc_contracts::generated_api::*;
use rust_decimal::Decimal;
use serde::Deserialize;
use serde_json::{Value, json};
use std::{collections::BTreeMap, sync::OnceLock};

#[derive(Deserialize)]
struct Field {
    key: String,
    label: String,
    target: String,
    #[serde(default)]
    format: String,
}
#[derive(Deserialize)]
struct Layout {
    marker: String,
    headers: Vec<Field>,
    items: Vec<Field>,
    producers: Vec<Field>,
}
fn layout() -> &'static Layout {
    static LAYOUT: OnceLock<Layout> = OnceLock::new();
    LAYOUT.get_or_init(|| {
        serde_json::from_str(include_str!(
            "../../../Resources/ExcelTemplates/single-window-coo-layout.json"
        ))
        .expect("bundled COO layout")
    })
}
fn table_headers(range: &Range<Data>, fields: &[Field], name: &str) -> Result<()> {
    for (index, field) in fields.iter().enumerate() {
        if import::at(range, 3, index + 1) != field.label {
            return Err(format!(
                "{name} 第 {} 列表头应为“{}”，请使用产地证模板。",
                index + 1,
                field.label
            ));
        }
    }
    Ok(())
}
fn row_values(range: &Range<Data>, row: usize, fields: &[Field]) -> Value {
    Value::Object(
        fields
            .iter()
            .enumerate()
            .map(|(col, field)| (field.key.clone(), json!(import::at(range, row, col + 1))))
            .collect(),
    )
}
fn text<'a>(value: &'a Value, key: &str) -> &'a str {
    value[key].as_str().unwrap_or("").trim()
}

pub fn preview(
    bytes: &[u8],
    name: &str,
    manual: Option<&Value>,
    business_date: &str,
    check: Check<'_>,
) -> Result<ApiSingleWindowExcelImportPreviewResponse> {
    let mut workbook = crate::reader::open(bytes, check)?;
    let names = workbook.sheet_names().to_vec();
    let native = ["基本资料", "商品明细", "生产企业"];
    if !native
        .iter()
        .all(|sheet| names.iter().any(|name| name == sheet))
    {
        // A native workbook with a missing sheet must never silently lose factory data.
        if names.iter().any(|name| name == "基本资料") {
            let base = workbook
                .worksheet_range("基本资料")
                .map_err(|error| error.to_string())?;
            if import::at(&base, 1, 1) == layout().marker {
                return Err("产地证模板须保留基本资料、商品明细和生产企业三个工作表。".into());
            }
        }
        return Ok(ApiSingleWindowExcelImportPreviewResponse {
            result: import::parse(workbook, name, manual, business_date, check)?.response,
            coo: None,
            ..Default::default()
        });
    }
    if manual.is_some() {
        return Err("产地证模板请使用智能识别；手动方案用于其他客户格式。".into());
    }
    let epoch = match &workbook {
        Sheets::Xls(book) => Some(book.has_1904_epoch()),
        Sheets::Xlsx(book) => Some(book.has_1904_epoch()),
        _ => None,
    };
    let mut total = 0;
    let mut read = |sheet: &str, maximum: u32| -> Result<Range<Data>> {
        check()?;
        let range = workbook
            .worksheet_range(sheet)
            .map_err(|error| error.to_string())?;
        let range = import::content_range(range, &mut total, check)?;
        if range
            .cells()
            .any(|(_, _, cell)| matches!(cell, Data::Error(_)))
        {
            return Err(format!("{sheet} 含错误单元格，请先修正工作簿。"));
        }
        if range
            .end()
            .is_some_and(|(row, col)| row >= maximum || col >= 128)
        {
            return Err(format!("{sheet} 超出 {maximum} 行或 128 列上限。"));
        }
        Ok(range)
    };
    let base = read(native[0], 100)?;
    let items = read(native[1], 5003)?;
    let factories = read(native[2], 1003)?;
    let layout = layout();
    if import::at(&base, 1, 1) != layout.marker {
        return Err("基本资料不是有效的产地证模板。".into());
    }
    table_headers(&items, &layout.items, native[1])?;
    table_headers(&factories, &layout.producers, native[2])?;
    let mut producer_map = BTreeMap::new();
    for row in 4..=factories.end().map_or(3, |(row, _)| row as usize + 1) {
        check()?;
        let values = row_values(&factories, row, &layout.producers);
        if values
            .as_object()
            .unwrap()
            .values()
            .all(|value| value == "")
        {
            continue;
        }
        let key = text(&values, "producerKey").to_owned();
        if key.is_empty() {
            return Err(format!("生产企业第 {row} 行缺少企业编号。"));
        }
        let mut profile = json!({});
        for field in &layout.producers {
            if field.target == "coo" {
                profile[&field.key] = values[&field.key].clone();
            }
        }
        profile["producer"] = json!(
            [
                text(&values, "producerNameEN"),
                text(&values, "producerAddressEN")
            ]
            .into_iter()
            .filter(|value| !value.is_empty())
            .collect::<Vec<_>>()
            .join("\n")
        );
        if producer_map.insert(key.clone(), profile).is_some() {
            return Err(format!(
                "生产企业编号“{key}”重复，请为每家企业填写唯一编号。"
            ));
        }
    }
    let mut mapping = json!({"sheetName":native[1],"itemsStartRow":4,"itemsEndRow":0});
    for (col, field) in layout.items.iter().enumerate() {
        if field.target == "invoice" {
            mapping[format!("{}Col", field.key)] = json!(col + 1);
        }
    }
    let mut parsed = import::parse(workbook, name, Some(&mapping), business_date, check)?;
    let mut coo = json!({"items":[]});
    let mut default_origin = String::new();
    let mut header = parsed
        .response
        .invoice
        .as_ref()
        .map(serde_json::to_value)
        .transpose()
        .map_err(|error| error.to_string())?;
    for (index, field) in layout.headers.iter().enumerate() {
        let row = index + 3;
        if import::at(&base, row, 1) != field.label {
            return Err(format!("基本资料第 {row} 行应为“{}”。", field.label));
        }
        let mut value = import::at(&base, row, 2);
        if field.format == "date" && !value.is_empty() {
            value = import::numeric_date(&base, &field.key, row, 2, epoch).unwrap_or(value);
            value = import::import_date(&value)
                .ok_or_else(|| format!("{}请填写 YYYY-MM-DD。", field.label))?
                .to_string();
        }
        if field.key == "defaultOrigin" {
            default_origin = value;
        } else if field.target == "coo" {
            coo[&field.key] = json!(value);
        } else if let Some(header) = &mut header {
            header[&field.key] = json!(value);
        }
    }
    let mut descriptions = Vec::new();
    let mut coo_items = Vec::new();
    for (index, row) in parsed.source_rows.iter().enumerate() {
        check()?;
        let values = row_values(&items, *row, &layout.items);
        if let Some(header) = &mut header {
            // Invoice defaults must not replace the declared COO origin or units.
            let invoice_item = &mut header["items"][index];
            let origin = text(&values, "origin");
            invoice_item["origin"] = json!(if origin.is_empty() {
                default_origin.as_str()
            } else {
                origin
            });
            for (english, chinese) in [("unitEN", "unitCN"), ("ctnUnitEN", "ctnUnitCN")] {
                let unit_en = text(&values, english);
                let unit_cn = text(&values, chinese);
                invoice_item[english] = json!(unit_en);
                invoice_item[chinese] = json!(if unit_cn.is_empty() {
                    export_doc_domain::single_window::mapping::unit(unit_en, false)
                } else {
                    unit_cn.into()
                });
            }
        }
        if text(&values, "quantity").is_empty() && parsed.response.errors.len() < 100 {
            parsed
                .response
                .errors
                .push(format!("商品明细第 {row} 行缺少数量。"));
        }
        let key = text(&values, "producerKey");
        let mut item = if key.is_empty() {
            json!({})
        } else {
            producer_map
                .get(key)
                .ok_or_else(|| format!("商品明细第 {row} 行的生产企业编号“{key}”不存在。"))?
                .clone()
        };
        let description = text(&item, "producer").to_owned();
        if !description.is_empty() && !descriptions.contains(&description) {
            descriptions.push(description);
        }
        for field in &layout.items {
            if field.target != "coo" {
                continue;
            }
            let mut value = text(&values, &field.key).to_owned();
            if field.format == "number" && !value.is_empty() {
                let amount = value
                    .parse::<Decimal>()
                    .map_err(|_| format!("商品明细第 {row} 行的 {} 不是有效数值。", field.label))?;
                if amount.is_sign_negative() || amount.scale() > 5 {
                    return Err(format!(
                        "商品明细第 {row} 行的 {} 须为非负数，最多五位小数。",
                        field.label
                    ));
                }
                value = amount.normalize().to_string();
            }
            item[&field.key] = json!(value);
        }
        coo_items.push(item);
    }
    // Any nonempty row omitted by the shared parser needs an explicit correction.
    for row in 4..=items.end().map_or(3, |(row, _)| row as usize + 1) {
        if parsed.source_rows.binary_search(&row).is_err()
            && row_values(&items, row, &layout.items)
                .as_object()
                .unwrap()
                .values()
                .any(|value| value != "")
        {
            parsed.response.errors.push(format!(
                "商品明细第 {row} 行未能导入，请填写品名或款号及有效数量。"
            ));
            if parsed.response.errors.len() >= 100 {
                break;
            }
        }
    }
    if text(&coo, "producer").is_empty() {
        coo["producer"] = json!(descriptions.join("\n"));
    }
    coo["items"] = json!(coo_items);
    export_doc_domain::single_window::validate_import(&coo)?;
    parsed.response.success = parsed.response.errors.is_empty();
    parsed.response.invoice = header
        .map(serde_json::from_value)
        .transpose()
        .map_err(|error| error.to_string())?;
    Ok(ApiSingleWindowExcelImportPreviewResponse {
        result: parsed.response,
        coo: Some(serde_json::from_value(coo).map_err(|error| error.to_string())?),
        ..Default::default()
    })
}
