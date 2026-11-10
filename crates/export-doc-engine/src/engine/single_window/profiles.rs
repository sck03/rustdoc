use super::*;
use crate::paths::{ensure_safe_absolute, nonce};
use base64::{Engine, prelude::BASE64_STANDARD};
use export_doc_single_window::{authentication, prefixed};
use std::path::{Path, PathBuf};

pub const OPERATIONS: &[Operation] = &[
    GET_SINGLE_WINDOW_CLIENT_PROFILES,
    SAVE_SINGLE_WINDOW_CLIENT_PROFILE,
    ACTIVATE_SINGLE_WINDOW_CLIENT_PROFILE,
];
pub const PROFILES: &str = "sw-profiles";

pub fn local(service: &NativeService) -> Result<()> {
    if service.provider()? != "SQLite" {
        return Err(super::super::error::unsupported(
            "持卡机档案及官方客户端交接仅在本机桌面可用。",
        ));
    }
    Ok(())
}

pub const BOXES: [&str; 4] = ["outBox", "sentBox", "inBox", "failBox"];

pub fn directory(value: &str) -> Result<PathBuf> {
    let path = PathBuf::from(value);
    ensure_safe_absolute(&path).map_err(invalid)?;
    match std::fs::metadata(&path) {
        Ok(meta) if meta.is_dir() => {}
        Ok(_) => return Err(invalid("客户端交接路径必须是文件夹。")),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
            return Err(invalid("客户端交接目录不存在，请重新检测或选择目录。"));
        }
        Err(e) => return Err(unavailable(format!("无法访问客户端交接目录：{e}"))),
    }
    Ok(path)
}

fn overlaps(left: &str, right: &str) -> bool {
    if left.is_empty() || right.is_empty() {
        return false;
    }
    let parts = |s: &str| {
        Path::new(s)
            .components()
            .map(|p| {
                let p = p.as_os_str().to_string_lossy();
                if cfg!(windows) {
                    p.to_lowercase()
                } else {
                    p.into_owned()
                }
            })
            .collect::<Vec<String>>()
    };
    let a = parts(left);
    let b = parts(right);
    a.starts_with(&b) || b.starts_with(&a)
}

pub fn active(tx: &Connection, actor: &Actor, service: &NativeService) -> Result<Value> {
    let _ = service;
    if tx.provider() != "SQLite" {
        return Err(super::super::error::unsupported(
            "持卡机操作仅在本机桌面可用。",
        ));
    }
    store::all(tx, PROFILES)?
        .into_iter()
        .find(|row| {
            row["isEnabled"] == true
                && row["isActive"] == true
                && auth::visible(actor, PERMISSION, "view", row)
        })
        .ok_or_else(|| error(409, "请先创建并启用当前公司与操作卡档案。"))
}

pub fn secret(service: &NativeService, profile: &Value) -> Result<zeroize::Zeroizing<String>> {
    service
        .protector
        .unprotect(
            &format!("sw-profile:{}", rules::text(profile, "profileKey")),
            rules::text(profile, "_secret"),
        )
        .map_err(unavailable)
}

pub fn assignment(service: &NativeService, profile: &Value) -> Result<Value> {
    let mut value = profile.clone();
    value["version"] = json!(1);
    value["authenticationSecret"] = json!(&*secret(service, profile)?);
    Ok(value)
}

fn response(tx: &Connection, service: &NativeService, actor: &Actor) -> Result<Value> {
    let mut profiles = Vec::new();
    let mut active = String::new();
    for mut row in store::all(tx, PROFILES)? {
        if row["isEnabled"] != true || !auth::visible(actor, PERMISSION, "view", &row) {
            continue;
        }
        if row["isActive"] == true {
            active = rules::text(&row, "profileKey").into();
        }
        row["stationAssignmentCode"] = json!(
            authentication::encode_assignment(&assignment(service, &row)?).map_err(unavailable)?
        );
        profiles.push(contracts::dto(
            contracts::schema("ApiSingleWindowClientProfileDto"),
            row,
        ));
    }
    profiles.sort_by(|a, b| {
        b["isActive"]
            .as_bool()
            .cmp(&a["isActive"].as_bool())
            .then_with(|| a["profileName"].as_str().cmp(&b["profileName"].as_str()))
    });
    Ok(
        json!({"profiles":profiles,"activeProfileKey":active,"storagePolicy":"档案及加密交接凭证保存在本机业务数据库；官方客户端的四类交接目录分别保存为本机绝对路径。","message":"持卡机操作档案已读取。"}),
    )
}

pub fn handle(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    body: &Value,
) -> Result<Value> {
    local(service)?;
    if operation == GET_SINGLE_WINDOW_CLIENT_PROFILES {
        return service.store.read_for(actor, operation, &[], |tx, actor| {
            response(tx, service, actor)
        });
    }
    service.store.transaction_for(actor, operation, &[], |tx, actor| {
        auth::authorize(actor, PERMISSION, "edit")?;
        let rows = store::all(tx, PROFILES)?;
        let mut value = if operation == ACTIVATE_SINGLE_WINDOW_CLIENT_PROFILE {
            rows.iter().find(|r| r["profileKey"] == parameter(parameters,"profileKey")).cloned()
                .ok_or_else(|| error(404,"操作档案不存在。"))?
        } else {
            export_doc_contracts::validation::structure(contracts::request(operation.id),body).map_err(invalid)?;
            let requested = rules::text(body,"profileKey");
            let previous = if requested.is_empty() { None } else {
                Some(rows.iter().find(|r| r["profileKey"] == requested).ok_or_else(|| error(404,"操作档案不存在。"))?)
            };
            let mut value = previous.cloned().unwrap_or_else(|| contracts::initial(contracts::schema("ApiSingleWindowClientProfileDto")));
            if let Some(previous) = previous {
                if !auth::visible(actor,PERMISSION,"edit",previous) { return Err(error(403,"无权修改该档案。")); }
                if body["expectedVersion"] != previous["versionNumber"] {
                    return Err(error(409,"操作档案已被修改，请刷新后重新编辑。"));
                }
                if previous["companyScope"] != body["companyScope"] || previous["cardIdentifier"] != body["cardIdentifier"] {
                    return Err(error(409,"档案创建后不能更换公司抬头或操作卡，请新增档案。"));
                }
            }
            for (key,max) in [("profileName",80),("companyScope",120),("cardIdentifier",120)] {
                let text = rules::text(body,key);
                if text.is_empty() || text.encode_utf16().count()>max || text.chars().any(char::is_control) { return Err(invalid("请填写有效的档案名称、公司抬头和操作卡标识。")); }
                value[key]=json!(text);
            }
            if !actor.admin && value["companyScope"] != actor.company { return Err(error(403,"无权为其它公司建立持卡机档案。")); }
            if body["canSubmitCustomsCoo"] != true && body["canSubmitAgentConsignment"] != true { return Err(invalid("请至少启用一种申报业务。")); }
            if previous.is_none() {
                if body["expectedVersion"] != 0 { return Err(invalid("新增操作档案的预期版本必须为 0。")); }
                if rows.len()>=100 { return Err(invalid("持卡机档案最多 100 个。")); }
                let station = match tx.settings("sw-station")? {
                    Some(v) if prefixed(rules::text(&v,"key"),"SWS-") => v,
                    Some(_) => return Err(unavailable("持卡机身份损坏。")),
                    None => { let v=json!({"key":format!("SWS-{}",nonce().map_err(unavailable)?.to_uppercase())}); tx.set_settings("sw-station",1,&v)?; v },
                };
                value["stationKey"]=station["key"].clone();
                value["profileKey"]=json!(format!("SWP-{}",nonce().map_err(unavailable)?.to_uppercase()));
                let mut bytes=zeroize::Zeroizing::new([0u8;32]);
                getrandom::fill(&mut *bytes).map_err(|_|unavailable("无法生成交接密钥。"))?;
                value["_secret"]=json!(service.protector.protect(&format!("sw-profile:{}",rules::text(&value,"profileKey")),&BASE64_STANDARD.encode(&*bytes)).map_err(unavailable)?);
                value["isEnabled"]=json!(true);
                value["isActive"]=json!(rows.is_empty());
            }
            let mut directories = Vec::new();
            for (field,enabled) in [("customsCooDirectories","canSubmitCustomsCoo"),("agentConsignmentDirectories","canSubmitAgentConsignment")] {
                value[enabled]=body[enabled].clone();
                value[field]=json!({});
                for key in BOXES {
                    let requested=rules::text(&body[field],key);
                    if body[enabled]==true {
                        let identity=directory(requested)?.canonicalize()?.to_string_lossy().into_owned();
                        if directories.iter().any(|other: &String| overlaps(other,&identity)) {
                            return Err(invalid("不同业务及不同用途的客户端目录不得相同或互相包含。"));
                        }
                        directories.push(identity);
                    }
                    value[field][key]=json!(requested);
                }
            }
            value
        };
        if operation==ACTIVATE_SINGLE_WINDOW_CLIENT_PROFILE {
            if !auth::visible(actor,PERMISSION,"edit",&value) { return Err(error(403,"无权启用该档案。")); }
            for mut row in rows.into_iter().filter(|r|r["isActive"]==true && r["id"]!=value["id"]) {
                row["isActive"]=json!(false);
                row["expectedVersion"]=row["versionNumber"].clone();
                let id=row["id"].as_i64().ok_or_else(||unavailable("档案编号损坏。"))?;
                store::save(tx,PROFILES,id,row,None,actor,"deactivate")?;
            }
            value["isActive"]=json!(true);
        }
        let id=value["id"].as_i64().unwrap_or(0);
        if id>0 { value["expectedVersion"]=if operation == ACTIVATE_SINGLE_WINDOW_CLIENT_PROFILE { value["versionNumber"].clone() } else { body["expectedVersion"].clone() }; }
        let scope=json!({"companyScope":value["companyScope"]});
        let identity=rules::text(&value,"profileKey").to_owned();
        store::save_in_scope(tx,PROFILES,id,value,Some(identity.clone()),actor,"save",Some(&scope))?;
        let mut result = response(tx,service,actor)?;
        result["savedProfileKey"] = json!(identity);
        Ok(result)
    })
}
