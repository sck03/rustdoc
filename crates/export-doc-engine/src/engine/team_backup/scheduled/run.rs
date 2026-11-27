use super::*;

pub(super) fn start(
    service: &NativeService,
    actor: &Actor,
    saved: &SavedPlan,
    id: String,
    password: zeroize::Zeroizing<String>,
    package_lock: fs::File,
) -> Result<Value> {
    let pg = service.provider()? == "PostgreSQL";
    let tool = if pg {
        Some(postgres::require_ready(service)?.pg_dump)
    } else {
        None
    };
    let (store, paths, protector, owner, saved) = (
        service.store.clone(),
        service.paths.clone(),
        service.protector.clone(),
        actor.id,
        saved.clone(),
    );
    service.jobs.start_backup(actor,RUN_BACKUP_PLAN,"ScheduledRecoveryBackup","创建恢复点",move |_|{
        let _package=package_lock;
        let result:Result<tasks::TaskOutput>=(||{
            administrator(&auth::current_actor(&store,owner)?)?;
            let _templates=crate::engine::report_template_files::storage_lock(&paths)?;
            if pg{postgres::ensure_no_pending(&paths)?;}else if super::super::disaster_root(&paths)?.join(".pending-restore.json").try_exists()?{return Err(error(409,"已有恢复等待维护处理。"));}
            let repository=points::open(&store,&paths,&protector,&saved,&password,true)?;
            let working=sealed::working_directory(&paths.cache_root)?;package::private_directory(&working)?;
            let capture:Result<_>=(||{
                let snapshot=working.join(if pg{"database.dump"}else{"snapshot.sqlite3"});
                if let Some(tool)=&tool{postgres::dump_to(&store,&protector,tool,&snapshot,&paths.disk_space)?;}else{super::super::snapshot::backup_sqlite(&store,&paths,&snapshot)?;}
                super::super::repository::capture::create(&repository,&store,&paths,&protector,&snapshot,if pg{"PostgreSQL"}else{"SQLite"},&id)
            })();
            let cleanup=fs::remove_dir_all(&working);let commit=capture?;cleanup?;
            if let Err(cause)=repository.retain(saved.plan.retention_count,saved.plan.retention_days) {
                finish_state(&store,&id,json!({"lastResult":"Partial","lastError":cause.message,"lastPointId":commit.id}))?;
                return Ok(tasks::TaskOutput{committed:true,file:None,managed_file:None,destination:None,directory:None,publication:None,archive:None,detail:"恢复点已提交；保留策略未完成，请检查灾备状态。".into()});
            }
            finish_state(&store,&id,json!({"lastResult":"Succeeded","lastError":"","lastPointId":commit.id,"repositoryId":commit.repository,"logicalBytes":commit.size_bytes,"newBytes":commit.new_bytes}))?;
            Ok(tasks::TaskOutput{committed:true,file:None,managed_file:None,destination:None,directory:None,publication:None,archive:None,detail:format!("恢复点已提交并核验：{} 个文件，新增 {} 字节。",commit.files,commit.new_bytes)})
        })();
        if let Err(cause)=&result {let _=finish_state(&store,&id,json!({"lastResult":"Failed","lastError":cause.message}));}
        result
    })
}
