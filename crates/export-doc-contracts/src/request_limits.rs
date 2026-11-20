//! Execution budgets shared by the HTTP host and generated browser clients.
pub const TRANSFER_SECONDS: u64 = 24 * 60 * 60;
pub const MAX_JSON_RESPONSE_BYTES: usize = 64 * 1024 * 1024;

#[derive(Clone, Copy, Debug, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Probe {
    Liveness,
    Readiness,
}

pub fn probe(operation: &str) -> Option<Probe> {
    match crate::contracts::contract()["operations"][operation]["probe"].as_str() {
        Some("liveness") => Some(Probe::Liveness),
        Some("readiness") => Some(Probe::Readiness),
        None => None,
        _ => panic!("invalid generated probe metadata"),
    }
}

/// Capacity is response metadata, not a path or authentication heuristic.
pub fn json_response_bytes(operation: &str) -> usize {
    crate::contracts::contract()["operations"][operation]["maxJsonResponseBytes"]
        .as_u64()
        .filter(|bytes| *bytes > 0 && *bytes <= MAX_JSON_RESPONSE_BYTES as u64)
        .expect("generated JSON response capacity") as usize
}
pub(crate) fn response_schema_bytes(schema: &str) -> usize {
    match schema {
        "ApiCommandResponse"
        | "ApiSupplierProductLinkDto"
        | "ApiSupplierAssessmentDto"
        | "ApiDownloadTicket"
        | "ApiLogoutResponse"
        | "ApiCrmCustomerBatchStatusResult" => 64 * 1024,
        "ApiSupplierAssessmentAnalyticsDto" => 1024 * 1024,
        // Text extraction is capped at 500k characters, including JSON escaping.
        "ApiLetterOfCreditImportResponse" => 4 * 1024 * 1024,
        // OCR's bounded worker response is at most 4 MiB; leave projection headroom.
        "ApiOcrRecognizeImageResponse" | "ApiLoginResponse" => 8 * 1024 * 1024,
        _ => MAX_JSON_RESPONSE_BYTES,
    }
}

/// The generated contract includes capability and binary-response overrides.
pub fn execution_seconds(operation: &str) -> u64 {
    crate::contracts::contract()["operations"][operation]["timeoutSeconds"]
        .as_u64()
        .filter(|seconds| *seconds > 0)
        .expect("generated operation execution budget")
}

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
        "UpdateFileStorageCredentials" | "GetFileStorage" => 120,
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
        assert_eq!(
            document["x-exportdoc-json-response-limit-bytes"],
            super::MAX_JSON_RESPONSE_BYTES
        );
        for operation in document["paths"]
            .as_object()
            .unwrap()
            .values()
            .flat_map(|path| path.as_object().unwrap().values())
        {
            let id = operation["operationId"].as_str().unwrap();
            assert_eq!(
                super::execution_seconds(id),
                operation["x-exportdoc-timeout-seconds"].as_u64().unwrap(),
                "{id}"
            );
        }
        for (id, expected) in [
            ("getHealth", 30),
            ("UploadLetterOfCreditDocument", 600),
            ("ReviewLetterOfCreditCompliance", 125),
            ("RecognizeOcrImage", 95),
            ("SendEmail", 65),
            ("DownloadJobResult", 86400),
            ("UploadPersonnelAttachment", 86400),
            ("UploadBusinessAttachment", 86400),
            ("DownloadPersonnelAttachment", 86400),
            ("UpdateFileStorageCredentials", 120),
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
