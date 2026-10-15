//! Authenticated task and backup downloads, including the maintenance read lock.
use super::{
    NativeService, backup_io,
    error::{Result, invalid, unavailable, unsupported},
    tasks, team_backup,
};
use crate::generated_api::{
    DOWNLOAD_JOB_RESULT, DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
    DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET, Operation,
};

impl NativeService {
    pub fn download_job(&self, job_id: &str, token: &str) -> Result<tasks::FileOutput> {
        self.prepare_job_download(job_id, token)?.into_memory()
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
        let job = self.jobs.get(&actor, job_id)?;
        if job["status"] != "Succeeded" || job["outputPath"].as_str().is_none_or(str::is_empty) {
            return Err(invalid("任务没有可下载的结果。"));
        }
        Ok(())
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
