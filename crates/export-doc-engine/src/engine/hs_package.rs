//! The original 1.0 knowledge package contract, with bounded ZIP validation.
use super::{
    NativeService,
    error::{Result, conflict, invalid, unavailable},
    hs, hs_learning, media,
    records::text,
    store::{self, Actor},
};
use crate::{contracts, operation, paths::valid_file_name};
use export_doc_domain::hs as rules;
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    io::{Cursor, Read, Write},
};
use zip::{ZipArchive, ZipWriter, write::SimpleFileOptions};
const MAX_PACKAGE: u64 = 100 * 1024 * 1024;
const MAX_EXPANDED: u64 = 300 * 1024 * 1024;
const CONTENTS: &[(&str, &str)] = &[
    ("hs-codes.json", hs::CODES),
    ("declaration-examples.json", hs::EXAMPLES),
    ("replacement-relations.json", hs::REPLACEMENTS),
    ("search-feedback.json", hs::FEEDBACK),
];

fn projected(kind: &str, mut row: Value) -> Value {
    if kind == hs::CODES {
        return contracts::project(contracts::schema("ApiHsCodeDto"), row);
    }
    if kind == hs::EXAMPLES {
        return contracts::project(contracts::schema("HsCodeDeclarationExample"), row);
    }
    if let Some(object) = row.as_object_mut() {
        object.retain(|key, _| {
            !matches!(
                key.as_str(),
                "ownerUserId" | "companyScope" | "departmentId" | "versionNumber" | "rowVersion"
            )
        });
    }
    row
}
pub fn export(service: &NativeService, query: &[(&str, String)]) -> Result<Vec<u8>> {
    let since = hs::read(query, "since");
    let since = if since.is_empty() {
        None
    } else {
        Some(
            chrono::DateTime::parse_from_rfc3339(since)
                .map_err(|_| invalid("增量导出起始时间无效。"))?,
        )
    };
    let snapshots = service.store.transaction(|tx| {
        CONTENTS
            .iter()
            .map(|(_, kind)| {
                store::all(tx, kind)?
                    .into_iter()
                    .filter_map(|row| {
                        if let Some(since) = since {
                            let date = row[if *kind == hs::CODES {
                                "updateTime"
                            } else {
                                "updatedAt"
                            }]
                            .as_str();
                            match date.map(chrono::DateTime::parse_from_rfc3339) {
                                Some(Ok(date)) if date < since => return None,
                                Some(Err(_)) => {
                                    return Some(Err(unavailable("知识记录更新时间无效。")));
                                }
                                _ => {}
                            }
                        }
                        Some(Ok(projected(kind, row)))
                    })
                    .collect::<Result<Vec<_>>>()
            })
            .collect::<Result<Vec<_>>>()
    })?;
    let mut archive = ZipWriter::new(Cursor::new(Vec::new()));
    let options = SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);
    let mut checksums = BTreeMap::new();
    let mut total = 0_u64;
    for ((name, _), rows) in CONTENTS.iter().zip(snapshots) {
        operation::check()?;
        let bytes = serde_json::to_vec(&rows)?;
        total += bytes.len() as u64;
        if bytes.len() as u64 > MAX_PACKAGE || total > MAX_EXPANDED {
            return Err(invalid("知识包导出超过容量上限，请使用增量导出。"));
        }
        checksums.insert(name, media::digest(&bytes));
        archive
            .start_file(*name, options)
            .map_err(|e| unavailable(e.to_string()))?;
        archive.write_all(&bytes)?;
    }
    let manifest = json!({"schemaVersion":"1.0","exportedAt":store::timestamp(),"since":since.map(|d|d.to_rfc3339()),"checksums":checksums});
    archive
        .start_file("manifest.json", options)
        .map_err(|e| unavailable(e.to_string()))?;
    archive.write_all(&serde_json::to_vec(&manifest)?)?;
    let bytes = archive
        .finish()
        .map_err(|e| unavailable(e.to_string()))?
        .into_inner();
    if bytes.len() as u64 > MAX_PACKAGE {
        return Err(invalid("知识包超过 100 MiB。"));
    }
    Ok(bytes)
}
fn read(bytes: &[u8]) -> Result<BTreeMap<String, Vec<Value>>> {
    if bytes.is_empty() || bytes.len() as u64 > MAX_PACKAGE {
        return Err(invalid("知识包为空或超过 100 MiB。"));
    }
    let mut archive =
        ZipArchive::new(Cursor::new(bytes)).map_err(|e| invalid(format!("知识包无效：{e}")))?;
    if archive.len() != 5 {
        return Err(invalid("知识包须包含清单和四类知识文件。"));
    }
    let mut entries = BTreeMap::new();
    let mut total = 0_u64;
    for index in 0..archive.len() {
        operation::check()?;
        let mut entry = archive
            .by_index(index)
            .map_err(|e| invalid(e.to_string()))?;
        let name = entry.name().to_owned();
        if !(name == "manifest.json" || CONTENTS.iter().any(|(key, _)| *key == name))
            || entries.contains_key(&name)
            || entry.is_dir()
            || entry
                .unix_mode()
                .is_some_and(|mode| mode & 0o170000 == 0o120000)
        {
            return Err(invalid("知识包包含不允许、重复或链接文件。"));
        }
        let maximum = if name == "manifest.json" {
            256 * 1024
        } else {
            MAX_PACKAGE
        };
        total = total
            .checked_add(entry.size())
            .ok_or_else(|| invalid("知识包容量无效。"))?;
        if entry.size() > maximum || total > MAX_EXPANDED {
            return Err(invalid("知识包解压容量超过上限。"));
        }
        let mut data = Vec::new();
        (&mut entry)
            .take(maximum + 1)
            .read_to_end(&mut data)
            .map_err(|e| invalid(e.to_string()))?;
        if data.len() as u64 > maximum {
            return Err(invalid("知识文件超过上限。"));
        }
        entries.insert(name, data);
    }
    let manifest: Value = serde_json::from_slice(
        entries
            .get("manifest.json")
            .ok_or_else(|| invalid("缺少知识包清单。"))?,
    )
    .map_err(|e| invalid(e.to_string()))?;
    if manifest["schemaVersion"] != "1.0" {
        return Err(invalid("知识包版本无效。"));
    }
    chrono::DateTime::parse_from_rfc3339(manifest["exportedAt"].as_str().unwrap_or(""))
        .map_err(|_| invalid("知识包导出时间无效。"))?;
    let mut result = BTreeMap::new();
    for (name, kind) in CONTENTS {
        let bytes = entries
            .get(*name)
            .ok_or_else(|| invalid("缺少知识文件。"))?;
        if !manifest["checksums"][*name]
            .as_str()
            .is_some_and(|digest| digest.eq_ignore_ascii_case(&media::digest(bytes)))
        {
            return Err(invalid("知识包校验和不匹配。"));
        }
        let rows: Vec<Value> =
            serde_json::from_slice(bytes).map_err(|e| invalid(format!("知识记录无效：{e}")))?;
        if rows.len()
            > if *kind == hs::CODES {
                500_000
            } else {
                1_000_000
            }
        {
            return Err(invalid("知识包记录数量超过上限。"));
        }
        result.insert((*kind).into(), rows);
    }
    Ok(result)
}
pub fn import(service: &NativeService, actor: &Actor, name: &str, bytes: &[u8]) -> Result<Value> {
    if !valid_file_name(name) {
        return Err(invalid("知识包文件名无效。"));
    }
    let content = read(bytes)?;
    service.store.transaction(|tx|{
        let mut counters=BTreeMap::<&str,usize>::new();let mut unique=BTreeSet::new();
        for mut row in content[hs::CODES].clone(){
            operation::check()?;let code=rules::code(&text(&row,"code")).map_err(invalid)?;
            if !unique.insert(code.clone()){return Err(invalid("知识包包含重复编码。"));}
            let old=tx.find_identity(hs::CODES,&code)?;
            if let Some(old)=&old{
                if old["status"]=="Active"&&(row["status"]!="Active"||row["effectiveYear"].as_i64()<old["effectiveYear"].as_i64()){continue;}
                row["expectedVersion"]=old["versionNumber"].clone();
            }
            *counters.entry(if old.is_some(){"updatedHsCodes"}else{"addedHsCodes"}).or_default()+=1;
            hs::save_code(tx,actor,&row,true)?;
        }
        unique.clear();
        for row in &content[hs::EXAMPLES]{
            operation::check()?;let raw=rules::code(&text(row,"rawReportedHsCode")).map_err(invalid)?;
            let fingerprint=rules::fingerprint(&[&raw,&text(row,"productName"),&text(row,"specification")]);
            if !unique.insert(fingerprint.clone()){return Err(invalid("知识包包含重复案例。"));}
            let old=tx.find_identity(hs::EXAMPLES,&fingerprint.to_lowercase())?;let mut input=row.clone();input["id"]=json!(0);
            if let Some(old)=&old{input["id"]=old["id"].clone();
                if old["isManuallyVerified"]==true{
                    if row["isManuallyVerified"]==true&&old["resolvedCurrentHsCode"]!=row["resolvedCurrentHsCode"]{return Err(conflict("导入案例与已有人工确认编码冲突。"));}
                    input["resolvedCurrentHsCode"]=old["resolvedCurrentHsCode"].clone();input["isManuallyVerified"]=json!(true);input["source"]=old["source"].clone();
                }
            }
            let saved=hs_learning::save_example(tx,actor,&input)?;let mut merged=saved.clone();
            for key in ["useCount","rejectedCount"]{let incoming=row[key].as_i64().unwrap_or(0);if incoming<0||incoming>i32::MAX as i64{return Err(invalid("案例学习次数无效。"));}merged[key]=json!(incoming.max(saved[key].as_i64().unwrap_or(0)));}
            if merged!=saved{store::save(tx,hs::EXAMPLES,saved["id"].as_i64().unwrap_or(0),merged,Some(fingerprint),actor,"edit")?;}
            *counters.entry(if old.is_some(){"updatedExamples"}else{"addedExamples"}).or_default()+=1;
        }
        for (kind,counter) in [(hs::REPLACEMENTS,"addedReplacements"),(hs::FEEDBACK,"addedFeedback")]{
            unique.clear();
            for row in &content[kind]{
                operation::check()?;let mut row=row.clone();
                let fingerprint=if kind==hs::REPLACEMENTS{
                    let old=rules::code(&text(&row,"oldCode")).map_err(invalid)?;let new=rules::code(&text(&row,"newCode")).map_err(invalid)?;
                    row["oldCode"]=json!(old);row["newCode"]=json!(new);
                    if row["confidence"].as_i64().is_some_and(|c|!(0..=100).contains(&c)){return Err(invalid("替代关系置信度无效。"));}
                    rules::fingerprint(&[&old,&new,&row["effectiveYear"].to_string()])
                }else{
                    let code=rules::code(&text(&row,"candidateCode")).map_err(invalid)?;row["candidateCode"]=json!(code);
                    for (key,max) in [("queryText",500),("productName",300),("specification",1500)]{if text(&row,key).chars().count()>max{return Err(invalid("学习记录字段过长。"));}}
                    for key in ["acceptedCount","rejectedCount"]{if row[key].as_i64().is_none_or(|n|n<0||n>i32::MAX as i64){return Err(invalid("学习记录计数无效。"));}}
                    rules::fingerprint(&[&rules::text(&text(&row,"queryText")),&code,&text(&row,"productName"),&text(&row,"specification")])
                };
                if !unique.insert(fingerprint.clone()){return Err(invalid("知识包包含重复的替代或学习记录。"));}
                if tx.find_identity(kind,&fingerprint.to_lowercase())?.is_some(){continue;}
                row["fingerprint"]=json!(fingerprint);row.as_object_mut().ok_or_else(||invalid("知识记录格式无效。"))?.remove("id");
                store::save(tx,kind,0,row,Some(fingerprint),actor,"create")?;*counters.entry(counter).or_default()+=1;
            }
        }
        let mut result=json!({"addedHsCodes":0,"updatedHsCodes":0,"addedExamples":0,"updatedExamples":0,"addedReplacements":0,"addedFeedback":0,"message":"HS 知识包已校验并导入。"});for (key,count) in counters{result[key]=json!(count);}
        Ok(json!({"fileName":name,"hsCodeCount":content[hs::CODES].len(),"exampleCount":content[hs::EXAMPLES].len(),"replacementCount":content[hs::REPLACEMENTS].len(),"feedbackCount":content[hs::FEEDBACK].len(),"warnings":[],"result":result}))
    })
}
