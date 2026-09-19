//! The public .edpkg interchange format: a bounded ZIP with data and checksum.
use super::super::{
    error::{Result, invalid},
    media,
};
use crate::contracts;
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    io::{Cursor, Read, Write},
};
use zip::{ZipArchive, ZipWriter, write::SimpleFileOptions};

pub const MAX_INPUT: usize = 25 * 1024 * 1024;
const MAX_DATA: usize = 32 * 1024 * 1024;
pub const ASSET_FIELDS: &[&str] = &["shippingMarksImage", "docSealPath", "customsSealPath"];
pub struct Package {
    pub invoice: Value,
    pub customer: Option<Value>,
    pub exporter: Option<Value>,
    pub assets: BTreeMap<String, Vec<u8>>,
    pub checksum_valid: bool,
}
fn check() -> Result<()> {
    crate::operation::check()
}
fn utf8(bytes: &[u8]) -> Result<&str> {
    std::str::from_utf8(bytes.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(bytes))
        .map_err(|_| invalid("单据包须为有效 UTF-8 文本。"))
}
// Wire property names belong to the interchange format; API names come from
// the generated schema, including nullable fields and notification enums.
fn api(schema: &Value, value: &Value) -> Result<Value> {
    let schema = contracts::resolve(schema);
    if value.is_null() {
        return Ok(contracts::initial(schema));
    }
    if let Some(properties) = schema["properties"].as_object() {
        let source = value
            .as_object()
            .ok_or_else(|| invalid("单据包对象结构无效。"))?;
        let mut output = contracts::initial(schema);
        for (name, definition) in properties {
            let mut matches = source
                .iter()
                .filter(|(key, _)| key.eq_ignore_ascii_case(name));
            if let Some((_, field)) = matches.next() {
                output[name] = api(definition, field)?;
            }
            if matches.next().is_some() {
                return Err(invalid("单据包字段重复。"));
            }
        }
        return Ok(output);
    }
    if contracts::kind(schema) == "array" {
        let rows = value
            .as_array()
            .filter(|rows| rows.len() <= 5000)
            .ok_or_else(|| invalid("单据包明细结构或容量无效。"))?;
        return rows
            .iter()
            .map(|v| api(&schema["items"], v))
            .collect::<Result<Vec<_>>>()
            .map(Value::Array);
    }
    if let (Some(options), Some(index)) = (schema["enum"].as_array(), value.as_u64()) {
        return options
            .get(index as usize)
            .cloned()
            .ok_or_else(|| invalid("单据包枚举值无效。"));
    }
    Ok(value.clone())
}
fn wire(value: &Value) -> Value {
    match value {
        Value::Object(fields) => Value::Object(
            fields
                .iter()
                .map(|(key, value)| {
                    let name = match key.as_str() {
                        "hsCode" => "HSCode".into(),
                        "gwPerCtn" => "GWPerCtn".into(),
                        "nwPerCtn" => "NWPerCtn".into(),
                        "gwTotal" => "GWTotal".into(),
                        "nwTotal" => "NWTotal".into(),
                        _ => key
                            .chars()
                            .next()
                            .map(|c| c.to_uppercase().collect::<String>() + &key[c.len_utf8()..])
                            .unwrap_or_default(),
                    };
                    let value = if key == "notifyPartyMode" {
                        json!(match value.as_str() {
                            Some("SameAsConsignee") => 1,
                            Some("Separate") => 2,
                            _ => 0,
                        })
                    } else {
                        wire(value)
                    };
                    (name, value)
                })
                .collect(),
        ),
        Value::Array(values) => Value::Array(values.iter().map(wire).collect()),
        _ => value.clone(),
    }
}
pub fn read(bytes: &[u8]) -> Result<Package> {
    check()?;
    if bytes.is_empty() || bytes.len() > MAX_INPUT {
        return Err(invalid("单据包为空或超过 25 MiB。"));
    }
    let mut archive =
        ZipArchive::new(Cursor::new(bytes)).map_err(|_| invalid("单据包不是有效 ZIP。"))?;
    if archive.len() > 16 {
        return Err(invalid("单据包包含过多文件。"));
    }
    let mut entries = BTreeMap::new();
    let mut total = 0;
    for index in 0..archive.len() {
        check()?;
        let mut entry = archive
            .by_index(index)
            .map_err(|_| invalid("单据包目录损坏。"))?;
        let name = entry.name().to_owned();
        let limit = match name.as_str() {
            "data.json" => MAX_DATA,
            "meta.json" => 256 * 1024,
            _ if name
                .strip_prefix("assets/")
                .is_some_and(crate::paths::valid_file_name) =>
            {
                5 * 1024 * 1024
            }
            _ => return Err(invalid("单据包包含未声明或不安全的文件。")),
        };
        if entry.is_dir()
            || entry.unix_mode().is_some_and(|m| m & 0o170000 == 0o120000)
            || entry.size() > limit as u64
            || entries.contains_key(&name)
        {
            return Err(invalid("单据包文件重复、类型错误或超过容量。"));
        }
        let mut content = vec![];
        (&mut entry)
            .take(limit as u64 + 1)
            .read_to_end(&mut content)
            .map_err(|_| invalid("单据包文件损坏。"))?;
        total += content.len();
        if content.len() > limit || total > 48 * 1024 * 1024 {
            return Err(invalid("单据包解压容量超限。"));
        }
        entries.insert(name, content);
    }
    let data = entries
        .remove("data.json")
        .ok_or_else(|| invalid("单据包缺少 data.json。"))?;
    let meta = entries
        .remove("meta.json")
        .ok_or_else(|| invalid("单据包缺少 meta.json。"))?;
    let source = utf8(&data)?;
    let root: Value =
        serde_json::from_str(source).map_err(|_| invalid("单据包数据 JSON 无效。"))?;
    let metadata: Value =
        serde_json::from_str(utf8(&meta)?).map_err(|_| invalid("单据包校验信息无效。"))?;
    if root["SchemaVersion"] != "1.0" || !root["Invoice"].is_object() || !root["Items"].is_array() {
        return Err(invalid("单据包版本或结构无效。"));
    }
    let checksum_valid = metadata["checksum"]
        .as_str()
        .is_some_and(|s| s.eq_ignore_ascii_case(&media::digest(source.as_bytes())));
    let mut invoice = api(contracts::schema("ApiInvoiceDetailDto"), &root["Invoice"])?;
    invoice["items"] = api(
        &json!({"type":"array","items":{"$ref":"#/components/schemas/ApiInvoiceItemDto"}}),
        &root["Items"],
    )?;
    if !matches!(invoice["type"].as_str(), Some("实际数据" | "报关数据")) {
        return Err(invalid("单据包类型只能为实际数据或报关数据。"));
    }
    let party = |key: &str, schema: &str| {
        if root[key].is_null() {
            Ok(None)
        } else {
            api(contracts::schema(schema), &root[key]).map(Some)
        }
    };
    let mut assets = BTreeMap::new();
    let mut used = BTreeSet::new();
    if let Some(resources) = root.get("Resources") {
        let resources = resources
            .as_object()
            .filter(|r| r.len() <= 3)
            .ok_or_else(|| invalid("单据包图片目录无效。"))?;
        for (field, metadata) in resources {
            if !ASSET_FIELDS.contains(&field.as_str()) {
                return Err(invalid("单据包图片用途无效。"));
            }
            let name = metadata["entryName"]
                .as_str()
                .ok_or_else(|| invalid("图片缺少包内名称。"))?;
            let bytes = entries
                .get(name)
                .ok_or_else(|| invalid("单据包缺少引用图片。"))?;
            if metadata["sha256"] != media::digest(bytes)
                || metadata["byteLength"].as_u64() != Some(bytes.len() as u64)
            {
                return Err(invalid("单据包图片摘要不匹配。"));
            }
            media::image_type(bytes, 5 * 1024 * 1024)?;
            assets.insert(field.clone(), bytes.clone());
            used.insert(name.to_owned());
        }
    }
    if entries.keys().any(|key| !used.contains(key)) {
        return Err(invalid("单据包包含没有引用的文件。"));
    }
    Ok(Package {
        invoice,
        customer: party("Customer", "ApiCustomerDto")?,
        exporter: party("Exporter", "ApiExporterDto")?,
        assets,
        checksum_valid,
    })
}
pub fn write(package: &Package) -> Result<Vec<u8>> {
    check()?;
    let mut invoice = wire(&package.invoice);
    let items = invoice
        .as_object_mut()
        .ok_or_else(|| invalid("发票结构无效。"))?
        .remove("Items")
        .unwrap_or(json!([]));
    let mut root = json!({"SchemaVersion":"1.0","AppVersion":env!("CARGO_PKG_VERSION"),"CreatedAt":super::super::store::timestamp(),"Invoice":invoice,"Items":items,
        "Customer":package.customer.as_ref().map(wire),"Exporter":package.exporter.as_ref().map(wire),"Resources":{}});
    let mut entries = BTreeMap::new();
    for (field, bytes) in &package.assets {
        let ext = if media::image_type(bytes, 5 * 1024 * 1024)? == "image/png" {
            "png"
        } else {
            "jpg"
        };
        let digest = media::digest(bytes);
        let name = format!("assets/{digest}.{ext}");
        root["Resources"][field] =
            json!({"entryName":name,"sha256":digest,"byteLength":bytes.len()});
        entries.insert(name, bytes.clone());
    }
    let data = serde_json::to_vec(&root)?;
    if data.len() > MAX_DATA {
        return Err(invalid("单据包数据超过容量。"));
    }
    entries.insert(
        "meta.json".into(),
        serde_json::to_vec(&json!({"checksum":media::digest(&data)}))?,
    );
    entries.insert("data.json".into(), data);
    let mut archive = ZipWriter::new(Cursor::new(vec![]));
    for (name, bytes) in entries {
        check()?;
        archive
            .start_file(
                name,
                SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated),
            )
            .map_err(|_| invalid("无法创建单据包。"))?;
        archive.write_all(&bytes)?;
    }
    let bytes = archive
        .finish()
        .map_err(|_| invalid("无法完成单据包。"))?
        .into_inner();
    if bytes.len() > MAX_INPUT {
        return Err(invalid("生成的单据包超过 25 MiB，请减少单据内容。"));
    }
    Ok(bytes)
}
