//! Execution budgets shared by the HTTP host and generated browser clients.
pub const TRANSFER_SECONDS: u64 = 24 * 60 * 60;

pub fn seconds(operation: &str) -> u64 {
    match operation {
        "StageServerMigrationRestore"
        | "UploadAndRestorePostgreSqlPhysicalBackup"
        | "DownloadJobResult"
        | "DownloadJobResultWithTicket"
        | "DownloadPostgreSqlPhysicalBackupWithTicket"
        | "DownloadRecoveryArchiveWithTicket"
        | "RestorePostgreSqlPhysicalBackup" => TRANSFER_SECONDS,
        "UploadLetterOfCreditDocument" | "ImportLetterOfCreditDocument" => 600,
        "ReviewLetterOfCreditCompliance" => 125,
        "UploadOcrImage" | "RecognizeOcrImage" => 95,
        "SendEmail" | "TestEmailConnection" => 65,
        _ => 30,
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn long_running_operations_publish_their_execution_budgets() {
        let document = crate::openapi::document();
        for (id, expected) in [
            ("getHealth", 30),
            ("UploadLetterOfCreditDocument", 600),
            ("ReviewLetterOfCreditCompliance", 125),
            ("RecognizeOcrImage", 95),
            ("SendEmail", 65),
            ("DownloadJobResult", 86400),
        ] {
            let operation = document["paths"]
                .as_object()
                .unwrap()
                .values()
                .flat_map(|path| path.as_object().unwrap().values())
                .find(|operation| operation["operationId"] == id)
                .unwrap();
            assert_eq!(operation["x-exportdoc-timeout-seconds"], expected, "{id}");
        }
    }
}
