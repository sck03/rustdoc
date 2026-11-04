//! Authenticated task and backup downloads, including the maintenance read lock.
use super::{
    NativeService, backup_io,
    error::{Result, unavailable, unsupported},
    team_backup,
};
use crate::generated_api::{
    DOWNLOAD_JOB_RESULT, DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
    DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET, Operation,
};

impl NativeService {
    pub fn prepare_file_download(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        token: &str,
    ) -> Result<backup_io::Download> {
        if super::oa::metadata(operation).is_some_and(|m| {
            matches!(
                m["action"].as_str(),
                Some("document-download" | "bundle-download")
            )
        }) {
            let _access = self
                .maintenance
                .read()
                .map_err(|_| unavailable("数据库维护状态异常。"))?;
            let actor = self.sessions.actor(&self.store, token)?;
            self.authorize_operation(&actor, operation, &[])?;
            return super::oa::prepare_download(self, &actor, operation, parameters);
        }
        let output = self.download_file(operation, parameters, token)?;
        let reservation = self.jobs.reserve_download(output.content.len(), false)?;
        Ok(backup_io::Download::Memory {
            output,
            reservation,
        })
    }
    pub fn prepare_job_download(&self, job_id: &str, token: &str) -> Result<backup_io::Download> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        self.authorize_operation(&actor, DOWNLOAD_JOB_RESULT, &[])?;
        self.jobs.prepare_download(&actor, job_id)
    }
    pub fn authorize_job_download(&self, job_id: &str, token: &str) -> Result<()> {
        let actor = self.sessions.actor(&self.store, token)?;
        self.authorize_operation(&actor, DOWNLOAD_JOB_RESULT, &[])?;
        self.jobs.authorize_download(&actor, job_id)
    }
    pub fn prepare_backup_download(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        token: &str,
    ) -> Result<backup_io::Download> {
        let _access = self
            .maintenance
            .read()
            .map_err(|_| unavailable("数据库维护状态异常。"))?;
        let actor = self.sessions.actor(&self.store, token)?;
        self.authorize_operation(&actor, operation, &[])?;
        match operation {
            DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET => {
                team_backup::postgres::prepare_download(self, parameters)
            }
            DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET => {
                team_backup::scheduled::download(self, &actor, parameters)
            }
            _ => Err(unsupported("此操作不是备份下载。")),
        }
    }
}
