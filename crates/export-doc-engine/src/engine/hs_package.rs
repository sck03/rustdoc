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
use sha2::Digest;
use std::{
    collections::{BTreeMap, BTreeSet},
    io::{Cursor, Write},
};
use zip::{ZipArchive, ZipWriter, write::SimpleFileOptions};
mod io;
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
pub fn export(service: &NativeService, actor: &Actor, query: &[(&str, String)]) -> Result<Vec<u8>> {
    let _memory = service.jobs.reserve_download(MAX_PACKAGE as usize, true)?;
    let since = hs::read(query, "since");
    let since = if since.is_empty() {
        None
    } else {
        Some(
            chrono::DateTime::parse_from_rfc3339(since)
                .map_err(|_| invalid("增量导出起始时间无效。"))?,
        )
    };
    let mut archive = ZipWriter::new_stream(crate::bounded_bytes::BoundedBytes {
        bytes: Vec::new(),
        limit: MAX_PACKAGE as usize,
    });
    let options = SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);
    let mut checksums = BTreeMap::new();
    let mut total = 0_u64;
    service.store.read_for(
        actor,
        crate::generated_api::EXPORT_HS_CODE_KNOWLEDGE,
        query,
        |tx, _| {
            for (name, kind) in CONTENTS {
                operation::check()?;
                archive
                    .start_file(*name, options)
                    .map_err(|e| invalid(e.to_string()))?;
                let budget = MAX_PACKAGE.min(MAX_EXPANDED - 256 * 1024 - total);
                let mut writer = io::EntryWriter {
                    output: &mut archive,
                    remaining: budget,
                    digest: sha2::Sha256::new(),
                };
                writer.write_all(b"[").map_err(|e| invalid(e.to_string()))?;
                let mut count = 0;
                let mut records = super::hs_queries::query(kind, &[]);
                records.since = hs::read(query, "since");
                super::hs_queries::visit(tx, &mut records, |row| {
                    if count
                        >= if *kind == hs::CODES {
                            500_000
                        } else {
                            1_000_000
                        }
                    {
                        return Err(invalid("知识包记录数量超过上限，请使用增量导出。"));
                    }
                    if count > 0 {
                        writer.write_all(b",").map_err(|e| invalid(e.to_string()))?;
                    }
                    serde_json::to_writer(&mut writer, &projected(kind, row))
                        .map_err(|e| invalid(e.to_string()))?;
                    count += 1;
                    Ok(())
                })?;
                writer.write_all(b"]").map_err(|e| invalid(e.to_string()))?;
                total += budget - writer.remaining;
                checksums.insert(
                    name,
                    writer
                        .digest
                        .finalize()
                        .iter()
                        .map(|b| format!("{b:02X}"))
                        .collect::<String>(),
                );
            }
            Ok(())
        },
    )?;
    let manifest = json!({"schemaVersion":"1.0","exportedAt":store::timestamp(),"since":since.map(|d|d.to_rfc3339()),"checksums":checksums});
    archive
        .start_file("manifest.json", options)
        .map_err(|e| unavailable(e.to_string()))?;
    archive.write_all(&serde_json::to_vec(&manifest)?)?;
    let bytes = archive
        .finish()
        .map_err(|e| invalid(e.to_string()))?
        .into_inner()
        .bytes;
    Ok(bytes)
}
pub fn import(service: &NativeService, actor: &Actor, name: &str, bytes: &[u8]) -> Result<Value> {
    let _memory = service.jobs.reserve_download(MAX_PACKAGE as usize, true)?;
    if !valid_file_name(name) {
        return Err(invalid("知识包文件名无效。"));
    }
    let (mut archive, manifest) = io::open(bytes)?;
    service.store.transaction_for(actor, crate::generated_api::IMPORT_HS_CODE_KNOWLEDGE, &[], |tx, actor|{
        let mut counters=BTreeMap::<&str,usize>::new();let mut unique=BTreeSet::new();
        let code_count=io::visit(&mut archive,&manifest,hs::CODES,|mut row| {
            operation::check()?;let code=rules::code(&text(&row,"code")).map_err(invalid)?;
            if !unique.insert(code.clone()){return Err(invalid("知识包包含重复编码。"));}
            let old=tx.find_identity(hs::CODES,&code)?;
            if let Some(old)=&old{
                if old["status"]=="Active"&&(row["status"]!="Active"||row["effectiveYear"].as_i64()<old["effectiveYear"].as_i64()){return Ok(());}
                row["expectedVersion"]=old["versionNumber"].clone();
            }
            *counters.entry(if old.is_some(){"updatedHsCodes"}else{"addedHsCodes"}).or_default()+=1;
            hs::save_code(tx,actor,&row,true)?;
            Ok(())
        })?;
        unique.clear();
        let example_count=io::visit(&mut archive,&manifest,hs::EXAMPLES,|row| {
            let row=&row;
            operation::check()?;let raw=rules::code(&text(row,"rawReportedHsCode")).map_err(invalid)?;
            let fingerprint=rules::fingerprint(&[&raw,&text(row,"productName"),&text(row,"specification")]);
            if !unique.insert(fingerprint.clone()){return Err(invalid("知识包包含重复案例。"));}
            let old=tx.find_identity(hs::EXAMPLES,&fingerprint.to_lowercase())?;let mut input=row.clone();input["id"]=json!(0);
            input["expectedVersion"]=json!(0);
            if let Some(old)=&old{input["id"]=old["id"].clone();input["expectedVersion"]=old["versionNumber"].clone();
                if old["isManuallyVerified"]==true{
                    if row["isManuallyVerified"]==true&&old["resolvedCurrentHsCode"]!=row["resolvedCurrentHsCode"]{return Err(conflict("导入案例与已有人工确认编码冲突。"));}
                    input["resolvedCurrentHsCode"]=old["resolvedCurrentHsCode"].clone();input["isManuallyVerified"]=json!(true);input["source"]=old["source"].clone();
                }
            }
            let saved=hs_learning::save_example(tx,actor,&input)?;let mut merged=saved.clone();
            for key in ["useCount","rejectedCount"]{let incoming=row[key].as_i64().unwrap_or(0);if incoming<0||incoming>i32::MAX as i64{return Err(invalid("案例学习次数无效。"));}merged[key]=json!(incoming.max(saved[key].as_i64().unwrap_or(0)));}
            if merged!=saved{store::save(tx,hs::EXAMPLES,saved["id"].as_i64().unwrap_or(0),merged,Some(fingerprint),actor,"edit")?;}
            *counters.entry(if old.is_some(){"updatedExamples"}else{"addedExamples"}).or_default()+=1;
            Ok(())
        })?;
        let mut counts=BTreeMap::new();
        for (kind,counter) in [(hs::REPLACEMENTS,"addedReplacements"),(hs::FEEDBACK,"addedFeedback")]{
            unique.clear();
            let count=io::visit(&mut archive,&manifest,kind,|mut row| {
                operation::check()?;
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
                if tx.find_identity(kind,&fingerprint.to_lowercase())?.is_some(){return Ok(());}
                row["fingerprint"]=json!(fingerprint);row.as_object_mut().ok_or_else(||invalid("知识记录格式无效。"))?.remove("id");
                store::save(tx,kind,0,row,Some(fingerprint),actor,"create")?;*counters.entry(counter).or_default()+=1;
                Ok(())
            })?;
            counts.insert(kind,count);
        }
        let mut result=json!({"addedHsCodes":0,"updatedHsCodes":0,"addedExamples":0,"updatedExamples":0,"addedReplacements":0,"addedFeedback":0,"message":"HS 知识包已校验并导入。"});for (key,count) in counters{result[key]=json!(count);}
        Ok(json!({"fileName":name,"hsCodeCount":code_count,"exampleCount":example_count,"replacementCount":counts[hs::REPLACEMENTS],"feedbackCount":counts[hs::FEEDBACK],"warnings":[],"result":result}))
    })
}
