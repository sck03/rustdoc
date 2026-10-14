//! Authenticate a bounded backup upload before its body is accepted, then recheck current access at staging.
use super::*;
use std::io::Read;

pub struct UploadAuthorization {
    actor_id: i64,
    operation: Operation,
    file_name: String,
    parameters: Vec<(String, String)>,
}
pub(super) fn begin(
    service: &NativeService,
    actor: &Actor,
    operation: Operation,
    parameters: &[(&str, String)],
    file_name: &str,
) -> Result<UploadAuthorization> {
    service.authorize_operation(actor, operation, &[])?;
    postgres::require_ready(service)?;
    postgres::ensure_no_pending(&service.paths)?;
    let (action, confirmation_text, extension) = if operation == STAGE_SERVER_MIGRATION_RESTORE {
        let password = header(parameters, MIGRATION_PASSWORD_HEADER);
        validate_package_password(password)?;
        (ACTION_RESTORE_SERVER, "MIGRATE", "edmmigration")
    } else if operation == UPLOAD_AND_RESTORE_POSTGRE_SQL_PHYSICAL_BACKUP {
        (ACTION_RESTORE_DATABASE, CONFIRM_RESTORE_DATABASE, "dump")
    } else {
        return Err(invalid("此操作不接受备份文件。"));
    };
    managed_file(&service.paths.cache_root, file_name, extension)?;
    confirmation(parameters, confirmation_text)?;
    sensitive_ticket(parameters, actor, action)?;
    Ok(UploadAuthorization {
        actor_id: actor.id,
        operation,
        file_name: file_name.into(),
        parameters: parameters
            .iter()
            .map(|(key, value)| ((*key).into(), value.clone()))
            .collect(),
    })
}
pub(super) fn finish(
    service: &NativeService,
    actor: &Actor,
    authorization: UploadAuthorization,
    content: impl Read,
) -> Result<Value> {
    if actor.id != authorization.actor_id {
        return Err(error(403, "上传会话已变化。"));
    }
    service.authorize_operation(actor, authorization.operation, &[])?;
    let parameters: Vec<_> = authorization
        .parameters
        .iter()
        .map(|(key, value)| (key.as_str(), value.clone()))
        .collect();
    if authorization.operation == STAGE_SERVER_MIGRATION_RESTORE {
        migration::stage_restore_upload(
            service,
            actor,
            &parameters,
            &authorization.file_name,
            content,
        )
    } else {
        postgres::upload_and_restore(
            service,
            actor,
            &parameters,
            &authorization.file_name,
            content,
        )
    }
}

impl NativeService {
    pub fn begin_backup_upload(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        file_name: &str,
        token: &str,
    ) -> Result<UploadAuthorization> {
        let actor = self.sessions.actor(&self.store, token)?;
        begin(self, &actor, operation, parameters, file_name)
    }
    pub fn finish_backup_upload(
        &self,
        authorization: UploadAuthorization,
        file: std::fs::File,
        token: &str,
    ) -> Result<Value> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        finish(self, &actor, authorization, file)
    }
}
