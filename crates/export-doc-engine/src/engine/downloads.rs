//! Authenticated task and backup downloads, including the maintenance read lock.
use super::{
    NativeService, backup_io,
    error::{Result, unsupported},
    team_backup,
};
use crate::generated_api::{
    DOWNLOAD_JOB_RESULT, DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET,
    DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET, Operation,
};

impl NativeService {
    fn with_download_access<T>(
        &self,
        operation: Operation,
        token: &str,
        work: impl FnOnce(&super::store::Actor) -> Result<T>,
    ) -> Result<T> {
        let _access = crate::engine::maintenance::acquire_read(&self.maintenance)?;
        let actor = self.sessions.actor(&self.store, token)?;
        self.authorize_operation(&actor, operation, &[])?;
        work(&actor)
    }
    pub fn prepare_file_download(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        token: &str,
    ) -> Result<backup_io::Download> {
        use crate::generated_api::{DOWNLOAD_BUSINESS_ATTACHMENT, DOWNLOAD_PERSONNEL_ATTACHMENT};
        if [DOWNLOAD_BUSINESS_ATTACHMENT, DOWNLOAD_PERSONNEL_ATTACHMENT].contains(&operation)
            || super::oa::is_download(operation)
                && super::oa::metadata(operation).is_some_and(|m| m["action"] == "download")
            || super::communication::is_download(operation)
        {
            let capture = || {
                self.with_download_access(operation, token, |actor| match operation {
                    DOWNLOAD_BUSINESS_ATTACHMENT => {
                        let id = super::records::id(parameters)?;
                        let revision = super::request_values::find(parameters, "revision")
                            .and_then(|v| v.parse().ok())
                            .ok_or_else(|| super::error::invalid("缺少有效资料版本号。"))?;
                        super::attachments::download_info(
                            &self.store,
                            actor,
                            operation,
                            id,
                            revision,
                        )
                    }
                    DOWNLOAD_PERSONNEL_ATTACHMENT => {
                        super::personnel_files::download_info(&self.store, actor, parameters)
                    }
                    _ => super::record_documents::download_info(self, actor, operation, parameters),
                })
            };
            let info = capture()?;
            let download = self.store.files.download(&info)?;
            if capture()? != info {
                return Err(super::error::conflict("文件已变化，请重新下载。"));
            }
            return Ok(download);
        }
        if super::oa::metadata(operation).is_some_and(|m| {
            matches!(
                m["action"].as_str(),
                Some("document-download" | "bundle-download")
            )
        }) {
            return self.with_download_access(operation, token, |actor| {
                super::oa::prepare_download(self, actor, operation, parameters)
            });
        }
        let output = self.download_file(operation, parameters, token)?;
        let reservation = self.jobs.reserve_download(output.content.len(), false)?;
        Ok(backup_io::Download::Memory {
            output,
            reservation,
        })
    }
    pub fn prepare_job_download(&self, job_id: &str, token: &str) -> Result<backup_io::Download> {
        let (pending, snapshot) =
            self.with_download_access(DOWNLOAD_JOB_RESULT, token, |actor| {
                self.jobs.capture_download(actor, job_id)
            })?;
        let download = pending.verify()?;
        self.with_download_access(DOWNLOAD_JOB_RESULT, token, |actor| {
            self.jobs
                .authorize_download(actor, job_id, Some(&snapshot))?;
            Ok(download)
        })
    }
    pub fn authorize_job_download(&self, job_id: &str, token: &str) -> Result<()> {
        let actor = self.sessions.actor(&self.store, token)?;
        self.authorize_operation(&actor, DOWNLOAD_JOB_RESULT, &[])?;
        self.jobs.authorize_download(&actor, job_id, None)
    }
    pub fn prepare_backup_download(
        &self,
        operation: Operation,
        parameters: &[(&str, String)],
        token: &str,
    ) -> Result<backup_io::Download> {
        self.with_download_access(operation, token, |actor| match operation {
            DOWNLOAD_POSTGRE_SQL_PHYSICAL_BACKUP_WITH_TICKET => {
                team_backup::postgres::prepare_download(self, parameters)
            }
            DOWNLOAD_RECOVERY_ARCHIVE_WITH_TICKET => {
                team_backup::scheduled::download(self, actor, parameters)
            }
            _ => Err(unsupported("此操作不是备份下载。")),
        })
    }
}
