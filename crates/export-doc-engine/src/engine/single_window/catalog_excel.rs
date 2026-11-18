//! Reference catalog Excel import preview. Mirrors the original C#
//! SingleWindowReferenceCatalogExcelImportService: the workbook is parsed,
//! columns are resolved (requested > header detected > sequential) and the
//! resulting catalog is returned for user review. Nothing is persisted.
use super::*;
use crate::{operation, paths::valid_file_name};
use export_doc_excel::{read_sheet_data, sheet_names};

pub const UPLOADS: &[Operation] = &[PREVIEW_SINGLE_WINDOW_REFERENCE_CATALOG_EXCEL_IMPORT];
const POLICY: &str = "预览不写入数据库；确认后通过申报词典保存写入当前业务数据库。";
const MAX_ROWS: usize = 5_000;

struct Field {
    key: &'static str,
    label: &'static str,
    required: bool,
    headers: &'static [&'static str],
}
struct Page {
    key: &'static str,
    fields: &'static [Field],
}
const fn field(
    key: &'static str,
    label: &'static str,
    required: bool,
    headers: &'static [&'static str],
) -> Field {
    Field {
        key,
        label,
        required,
        headers,
    }
}
const CODE: Field = field("code", "代码", true, &["编码"]);
const ENGLISH_NAME: Field = field("englishName", "英文名", true, &["英文名称"]);
const ALIASES: Field = field("aliases", "别名", false, &["AliasesText", "别称"]);
const PAGES: &[Page] = &[
    Page {
        key: "countries",
        fields: &[
            CODE,
            ENGLISH_NAME,
            field("chineseName", "中文名", true, &["中文名称"]),
            ALIASES,
        ],
    },
    Page {
        key: "acdCountries",
        fields: &[
            CODE,
            field("chineseName", "中文简称", true, &["中文名", "中文名称"]),
            ENGLISH_NAME,
            ALIASES,
        ],
    },
    Page {
        key: "currencies",
        fields: &[
            field("code", "标准数字代码", true, &["数字代码", "币制代码"]),
            field(
                "acdCode",
                "ACD海关币制码",
                false,
                &["海关币制码", "ACD币制码"],
            ),
            field("alphaCode", "字母代码", true, &["ISO代码", "币制字母代码"]),
            ALIASES,
        ],
    },
    Page {
        key: "acdTradeModes",
        fields: &[
            CODE,
            field("name", "简称", true, &["名称"]),
            field("description", "说明", false, &["描述", "备注"]),
            ALIASES,
        ],
    },
    Page {
        key: "transportModes",
        fields: &[
            field("value", "标准值", true, &["运输方式", "运输方式标准值"]),
            ALIASES,
        ],
    },
    Page {
        key: "ports",
        fields: &[
            field("value", "标准值", true, &["港口", "港口标准值"]),
            ALIASES,
        ],
    },
];

fn page(catalog_key: &str) -> Result<&'static Page> {
    PAGES
        .iter()
        .find(|page| page.key.eq_ignore_ascii_case(catalog_key.trim()))
        .ok_or_else(|| invalid("参考词典分类无效。"))
}
fn number(values: &Value, name: &str) -> i64 {
    values[name]
        .as_i64()
        .filter(|value| *value > 0)
        .unwrap_or(0)
}
use crate::engine::records::text;
fn normalize_header(value: &str) -> String {
    value
        .chars()
        .filter(|character| character.is_alphanumeric())
        .map(|character| character.to_ascii_lowercase())
        .collect()
}
fn check() -> std::result::Result<(), String> {
    operation::check().map_err(|cause| cause.to_string())
}
fn cell(row: &[String], column: usize) -> String {
    row.get(column.wrapping_sub(1))
        .map(|value| value.trim().to_string())
        .unwrap_or_default()
}
fn aliases(value: &str) -> Vec<String> {
    value
        .split([',', '，', ';', '；', '\r', '\n'])
        .map(str::trim)
        .filter(|item| !item.is_empty())
        .map(str::to_string)
        .collect::<std::collections::BTreeSet<_>>()
        .into_iter()
        .collect()
}
fn has_any(values: &[String]) -> bool {
    values.iter().any(|value| !value.is_empty())
}
fn read_cell(
    row: &[String],
    column_map: &std::collections::BTreeMap<String, usize>,
    field: &str,
) -> String {
    column_map
        .get(field)
        .filter(|column| **column > 0)
        .map(|column| cell(row, *column))
        .unwrap_or_default()
}
fn detect_column_map(
    header: &[String],
    page: &'static Page,
) -> std::collections::BTreeMap<String, usize> {
    let header: Vec<_> = header.iter().map(|value| normalize_header(value)).collect();
    page.fields
        .iter()
        .filter_map(|field| {
            let aliases: Vec<_> = [field.key, field.label]
                .into_iter()
                .chain(field.headers.iter().copied())
                .map(|name| normalize_header(name))
                .collect();
            header
                .iter()
                .position(|value| !value.is_empty() && aliases.contains(value))
                .map(|index| (field.key.to_owned(), index + 1))
        })
        .collect()
}
fn import_rows(
    page: &'static Page,
    rows: &[Vec<String>],
    column_map: &std::collections::BTreeMap<String, usize>,
) -> (Value, i64) {
    let entry = |row: &[String]| -> (Vec<String>, Value) {
        match page.key {
            "countries" => {
                let code = read_cell(row, column_map, "code");
                let english = read_cell(row, column_map, "englishName");
                let chinese = read_cell(row, column_map, "chineseName");
                let names = aliases(&read_cell(row, column_map, "aliases"));
                let values = [
                    code.clone(),
                    english.clone(),
                    chinese.clone(),
                    names.join(","),
                ];
                (
                    values.to_vec(),
                    json!({"code":code,"englishName":english,"chineseName":chinese,"aliases":names}),
                )
            }
            "acdCountries" => {
                let code = read_cell(row, column_map, "code");
                let chinese = read_cell(row, column_map, "chineseName");
                let english = read_cell(row, column_map, "englishName");
                let names = aliases(&read_cell(row, column_map, "aliases"));
                let values = [
                    code.clone(),
                    chinese.clone(),
                    english.clone(),
                    names.join(","),
                ];
                (
                    values.to_vec(),
                    json!({"code":code,"chineseName":chinese,"englishName":english,"aliases":names}),
                )
            }
            "currencies" => {
                let code = read_cell(row, column_map, "code");
                let acd = read_cell(row, column_map, "acdCode");
                let alpha = read_cell(row, column_map, "alphaCode");
                let names = aliases(&read_cell(row, column_map, "aliases"));
                let values = [code.clone(), acd.clone(), alpha.clone(), names.join(",")];
                (
                    values.to_vec(),
                    json!({"code":code,"acdCode":acd,"alphaCode":alpha,"aliases":names}),
                )
            }
            "acdTradeModes" => {
                let code = read_cell(row, column_map, "code");
                let name = read_cell(row, column_map, "name");
                let description = read_cell(row, column_map, "description");
                let names = aliases(&read_cell(row, column_map, "aliases"));
                let values = [
                    code.clone(),
                    name.clone(),
                    description.clone(),
                    names.join(","),
                ];
                (
                    values.to_vec(),
                    json!({"code":code,"name":name,"description":description,"aliases":names}),
                )
            }
            _ => {
                let value = read_cell(row, column_map, "value");
                let names = aliases(&read_cell(row, column_map, "aliases"));
                let values = [value.clone(), names.join(",")];
                (values.to_vec(), json!({"value":value,"aliases":names}))
            }
        }
    };
    let mut items = vec![];
    for row in rows {
        let (values, item) = entry(row);
        if has_any(&values) {
            items.push(item);
        }
    }
    let count = items.len() as i64;
    let catalog = match page.key {
        "countries" => json!({"countries":items}),
        "acdCountries" => json!({"acdCountries":items}),
        "currencies" => json!({"currencies":items}),
        "acdTradeModes" => json!({"acdTradeModes":items}),
        "transportModes" => json!({"transportModes":items}),
        _ => json!({"ports":items}),
    };
    (catalog, count)
}
pub fn preview(
    _service: &NativeService,
    _actor: &Actor,
    _operation: Operation,
    metadata: &Value,
    name: &str,
    bytes: &[u8],
) -> Result<Value> {
    if !valid_file_name(name) {
        return Err(invalid("导入文件名无效。"));
    }
    let extension = std::path::Path::new(name)
        .extension()
        .and_then(|extension| extension.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    if extension != "xlsx" && extension != "xlsm" {
        return Err(invalid("参考词典 Excel 导入只支持 .xlsx 或 .xlsm 文件。"));
    }
    let page = page(&text(metadata, "catalogKey"))?;
    let sheet = text(metadata, "sheetName");
    let data_start = number(metadata, "dataStartRowNumber");
    let data_start = if data_start > 0 { data_start } else { 2 };
    let header_row = number(metadata, "headerRowNumber");
    let header_row = if header_row > 0 {
        header_row
    } else {
        data_start.saturating_sub(1).max(1)
    };
    if data_start <= header_row {
        return Err(invalid("数据起始行必须大于表头行。"));
    }
    let names = sheet_names(bytes, name, &check).map_err(invalid)?;
    let table = read_sheet_data(bytes, &sheet, MAX_ROWS, &check).map_err(invalid)?;
    let first_row = table.first_row.max(1) as i64;
    let header = usize::try_from(header_row - first_row)
        .ok()
        .and_then(|index| table.rows.get(index));
    let detected = detect_column_map(header.map(Vec::as_slice).unwrap_or_default(), page);
    let column_map = page
        .fields
        .iter()
        .enumerate()
        .map(|(index, field)| {
            let column = metadata[format!("{}Column", field.key)]
                .as_u64()
                .and_then(|n| usize::try_from(n).ok())
                .filter(|n| *n > 0)
                .or_else(|| detected.get(field.key).copied())
                .unwrap_or(index + 1);
            (field.key.to_owned(), column)
        })
        .collect();
    let start = data_start.saturating_sub(first_row).max(0) as usize;
    let (catalog, row_count) = import_rows(
        page,
        table.rows.get(start..).unwrap_or_default(),
        &column_map,
    );
    let mappings: Vec<Value> = page
        .fields
        .iter()
        .map(|field| {
            json!({
                "fieldKey": field.key,
                "label": field.label,
                "columnNumber": column_map.get(field.key).copied().unwrap_or(0) as i64,
                "required": field.required,
            })
        })
        .collect();
    Ok(json!({
        "success": true,
        "catalogKey": page.key,
        "sheetName": table.sheet_name,
        "sheetNames": names,
        "headerRowNumber": header_row as i64,
        "dataStartRowNumber": data_start as i64,
        "columnMappings": mappings,
        "catalog": catalog,
        "rowCount": row_count,
        "message": "预览成功，请确认后再保存到申报词典。",
        "storagePolicy": POLICY,
    }))
}
