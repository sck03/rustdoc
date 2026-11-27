use super::*;
use crate::engine::error::conflict;
const CACHE: &str = "recovery-repository-maintenance";
const ROTATION: &str = "recovery-repository-password-change";
pub(in crate::engine) fn maintenance_status(
    service: &NativeService,
    actor: &Actor,
) -> Result<Value> {
    administrator(actor)?;
    Ok(service.store.settings(CACHE)?.unwrap_or_else(|| json!({})))
}
pub(in crate::engine) fn maintain(
    service: &NativeService,
    actor: &Actor,
    body: &Value,
) -> Result<Value> {
    administrator(actor)?;
    let saved = load(&service.store)?;
    let (store, paths, protector, owner, body) = (
        service.store.clone(),
        service.paths.clone(),
        service.protector.clone(),
        actor.id,
        body.clone(),
    );
    service.jobs.start_backup(actor,MAINTAIN_RECOVERY_REPOSITORY,"RecoveryRepositoryMaintenance","维护恢复仓库",move |_|{
        let _lock=package::lock(&paths)?;let actor=auth::current_actor(&store,owner)?;administrator(&actor)?;
        let password=protector.unprotect(PASSWORD,&saved.password).map_err(unavailable)?;let fenced=body["action"]=="RecoverLock" && body["oldInstanceIsolated"]==true;let repository=points::open_with_fence(&store,&paths,&protector,&saved,&password,false,fenced)?;
        let mut result=match body["action"].as_str().unwrap_or(""){
            "Preview"=>{let mut preview=repository.preview_gc()?;preview["message"]=json!("预览有效期 15 分钟；新发现的无引用对象至少隔离 24 小时后才可删除。");preview},
            "Collect"=>{let result=repository.execute_gc(body["token"].as_str().unwrap_or(""))?;json!({"count":result["count"],"bytes":result["bytes"],"message":"仓库回收已完成；有效恢复点和暂停保留对象保持受保护。"})},
            "Hold"|"Release"=>{repository.hold(body["pointId"].as_str().unwrap_or(""),body["reason"].as_str().unwrap_or(""),body["action"]=="Hold")?;json!({"message":"恢复点暂停保留状态已更新。"})},
            "RecoverLock"=>{if body["oldInstanceIsolated"]!=true{return Err(invalid("请先确认旧写者、恢复和导出实例均已停止并隔离。"));}repository.release_stale_protection()?;json!({"message":"已解除中断操作的保护；请重新完整预检。"})},
            "RotatePassword"=>{
                let password=zeroize::Zeroizing::new(body["newPassword"].as_str().unwrap_or("").to_owned());let protected=protector.protect(PASSWORD,&password).map_err(unavailable)?;
                store.transaction_as(&actor,|tx,_|{tx.set_settings(ROTATION,1,&json!({"repository":saved.repository,"revision":saved.revision,"password":protected}))?;Ok(())})?;
                repository.rotate_password(&password)?;
                store.transaction_as(&actor,|tx,_|{let mut current:SavedPlan=serde_json::from_value(tx.settings(PLAN)?.ok_or_else(||invalid("灾备计划缺失。"))?)?;if current.revision!=saved.revision{return Err(conflict("计划已变化，新凭证保存在恢复记录中，请重新启动以完成同步。"));}current.password=protected;current.revision+=1;tx.set_settings(PLAN,current.revision,&serde_json::to_value(current)?)?;tx.set_settings(ROTATION,2,&Value::Null)?;Ok(())})?;
                json!({"message":"恢复凭证已重新包裹并核验。请在 A 之外保存新凭证；已有便携文件仍使用导出时的凭证。"})
            },
            "Verify"=>{let mut cursor=String::new();let mut count=0;loop{let page=repository.list(&cursor,100)?;if page.is_empty(){break;}for point in page{cursor=format!("point-{}.commit",point.id);repository.verify(&point.id,&mut Verify)?;count+=1;}}json!({"count":count,"verifiedAt":Utc::now().to_rfc3339(),"message":"所有当前有效恢复点已完整核验。"})},
            _=>return Err(invalid("恢复仓库维护操作无效。")),
        };
        store.transaction_as(&actor,|tx,actor|{if result.get("verifiedAt").is_none(){if let Some(at)=tx.settings(CACHE)?.and_then(|v|v.get("verifiedAt").cloned()){result["verifiedAt"]=at;}}tx.set_settings(CACHE,1,&result)?;tx.append_audit_details(&AuditWrite{kind:"recovery-repository",record_id:0,version:saved.revision,action:body["action"].as_str().unwrap_or(""),actor_id:actor.id,occurred_at:&store::timestamp(),note:body["reason"].as_str().unwrap_or("")},&json!({"repository":saved.repository,"result":result}))?;Ok(())})?;
        Ok(tasks::TaskOutput{committed:true,detail:result["message"].as_str().unwrap_or("仓库维护已完成。").into(),file:None,destination:None,directory:None,managed_file:None,publication:None,archive:None})
    })
}
struct Verify;
impl super::super::repository::Visitor for Verify {
    fn begin(&mut self, _: &str, _: &str, _: u64, _: &str) -> Result<()> {
        Ok(())
    }
    fn data(&mut self, _: &[u8]) -> Result<()> {
        Ok(())
    }
    fn end(&mut self) -> Result<()> {
        Ok(())
    }
}
pub(super) fn recover_rotation(service: &NativeService) -> Result<()> {
    let Some(pending) = service.store.settings(ROTATION)?.filter(|v| v.is_object()) else {
        return Ok(());
    };
    let mut saved = load(&service.store)?;
    if pending["repository"] != saved.repository {
        return Err(invalid("未完成的凭证更换与当前仓库不符，请核对维护记录。"));
    }
    let _lock = package::lock(&service.paths)?;
    let protected = pending["password"]
        .as_str()
        .ok_or_else(|| invalid("凭证更换恢复记录损坏。"))?;
    let password = service
        .protector
        .unprotect(PASSWORD, protected)
        .map_err(unavailable)?;
    match points::open(
        &service.store,
        &service.paths,
        &service.protector,
        &saved,
        &password,
        false,
    ) {
        Ok(_) => {
            saved.password = protected.into();
            saved.revision += 1;
        }
        Err(_) => {
            let old = service
                .protector
                .unprotect(PASSWORD, &saved.password)
                .map_err(unavailable)?;
            points::open(
                &service.store,
                &service.paths,
                &service.protector,
                &saved,
                &old,
                false,
            )?;
        }
    }
    service.store.transaction(|tx| {
        tx.set_settings(PLAN, saved.revision, &serde_json::to_value(saved)?)?;
        tx.set_settings(ROTATION, 2, &Value::Null)?;
        Ok(())
    })
}
