//! Original settings categories with uncommon sections initially collapsed.
use crate::form_sections::{self, Section};
use export_doc_engine::contracts;
use serde_json::Value;

pub const CATEGORIES: &[(&str, &str)] = &[
    ("runtime", "运行与数据库"),
    ("documents", "单据设置"),
    ("excel-import", "Excel 导入"),
    ("exchange-rate", "汇率与币制"),
    ("communication", "邮件设置"),
    ("backup", "备份与恢复"),
    ("ai", "AI 服务"),
    ("maintenance", "维护工具"),
];
pub fn sections(category: &str) -> Vec<Section> {
    match category {
        "runtime" => vec![
            Section::new("settings.general", "常规设置", &["system.appName"], true),
            Section::new(
                "settings.paths",
                "文件与导出位置",
                &["system.defaultExportDirectory"],
                false,
            ),
            Section::new(
                "settings.updates",
                "更新设置",
                &["system.updaterEndpoint"],
                false,
            ),
        ],
        "documents" => vec![
            Section::new(
                "settings.documents",
                "单据与商品录入",
                &[
                    "system.defaultTemplateExporterNameCn",
                    "system.itemEntryBlankRowCount",
                    "system.itemEntrySpareColumnCount",
                ],
                true,
            ),
            Section::new(
                "settings.field-labels",
                "自定义字段名称",
                &["system.documentFieldLabels"],
                false,
            ),
            Section::new("settings.batch", "批量导出设置", &["batchExport"], false),
            Section::new(
                "settings.single-window",
                "申报默认资料",
                &["singleWindow"],
                false,
            ),
        ],
        "excel-import" => vec![
            Section::new("settings.excel", "导入字段映射", &["excelImport"], false),
            Section::new(
                "settings.excel-schemes",
                "其他导入方案",
                &["excelImportSchemes"],
                false,
            ),
        ],
        "exchange-rate" => vec![Section::new(
            "settings.rates",
            "汇率与币制",
            &["exchangeRate"],
            true,
        )],
        "communication" => vec![
            Section::new(
                "settings.email",
                "邮件服务器",
                &[
                    "email.smtpHost",
                    "email.smtpPort",
                    "email.enableSsl",
                    "email.userName",
                    "email.password",
                    "email.fromAddress",
                    "email.fromDisplayName",
                ],
                true,
            ),
            Section::new(
                "settings.recipients",
                "收件人规则",
                &["email.recipientAllowList", "email.recipientBlockList"],
                false,
            ),
            Section::new(
                "settings.mail-content",
                "默认邮件内容",
                &[
                    "email.documentEmailSubjectTemplate",
                    "email.documentEmailBodyTemplate",
                ],
                false,
            ),
        ],
        "backup" => vec![
            Section::new(
                "settings.backup",
                "备份保留",
                &["system.backupRetentionDays"],
                true,
            ),
            Section::new("settings.cloud-backup", "远程备份", &["webDav"], false),
        ],
        "ai" => vec![
            Section::new(
                "settings.ai",
                "AI 服务连接",
                &["ai.apiEndpoint", "ai.apiKey", "ai.modelName"],
                true,
            ),
            Section::new(
                "settings.ai-prompt",
                "提示词与高级设置",
                &["ai.systemPrompt"],
                false,
            ),
        ],
        "maintenance" => vec![
            Section::new(
                "settings.retention",
                "日志保留",
                &["system.auditLogRetentionDays", "system.logRetentionDays"],
                true,
            ),
            Section::new(
                "settings.log-limits",
                "日志容量限制",
                &["system.logRetainedFileCount", "system.logFileSizeLimitMB"],
                false,
            ),
        ],
        _ => vec![],
    }
}
pub fn schema(category: &str) -> Value {
    let definitions = sections(category);
    let fields: Vec<_> = definitions
        .iter()
        .flat_map(|section| section.fields.iter().copied())
        .collect();
    form_sections::schema(contracts::schema("AppSettings"), "", &fields)
}
