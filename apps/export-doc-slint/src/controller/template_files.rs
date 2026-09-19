use super::*;
use crate::{TemplateFileRow, TemplateFiles};
use export_doc_engine::generated_api::{
    ApiReportTemplateContentDto, ApiReportTemplateFileExportResponse,
    ApiReportTemplatePackageImportResponse, ApiReportTemplateStorageStatusResponse,
};
use serde_json::{Value, json};
use std::path::PathBuf;

const FILE_LIMIT: u64 = 16 * 1024 * 1024;
const PACKAGE_LIMIT: u64 = 64 * 1024 * 1024;

/// File mutations validate a content revision. The revision is only published
/// by content responses, so a mutation may have to load content first.
#[derive(Clone)]
pub enum PendingTemplate {
    Request {
        operation: Operation,
        body: Value,
        reply: String,
    },
    Upload {
        metadata: Value,
        source: PathBuf,
    },
}

impl Desktop {
    fn report_type(&self) -> String {
        self.ui
            .upgrade()
            .map(|ui| ui.global::<App>().get_report_type().to_string())
            .unwrap_or_else(|| "ExportDocument".into())
    }
    fn selected_template(&self) -> Option<ApiReportTemplateDto> {
        let index = self
            .ui
            .upgrade()
            .map(|ui| ui.global::<TemplateFiles>().get_selected())
            .unwrap_or(0);
        self.templates
            .get(index.max(1) as usize - 1)
            .filter(|template| !template.template_path.is_empty())
            .cloned()
    }
    pub fn refresh_template_files(&mut self) {
        if self.task.is_some() {
            return;
        }
        self.request(
            CHECK_REPORT_TEMPLATE_STORAGE,
            0,
            vec![],
            None,
            "template-files:storage",
        );
    }
    pub fn templates_loaded(&mut self, value: Value) {
        match serde_json::from_value::<Vec<ApiReportTemplateDto>>(value) {
            Ok(templates) => {
                self.templates = templates;
                self.document_package.load(&self.templates);
                if let Some(ui) = self.ui.upgrade() {
                    let app = ui.global::<App>();
                    app.set_report_template_index(0);
                    app.set_report_templates(model(
                        self.templates
                            .iter()
                            .map(|template| template.display_name.clone().into())
                            .collect(),
                    ));
                }
                self.sync_template_files();
                self.sync_document_package();
            }
            Err(cause) => self.error(cause.to_string()),
        }
    }
    pub fn sync_template_files(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<TemplateFiles>();
        let default_path = self
            .form
            .as_ref()
            .and_then(|form| {
                form.value["reportTemplateDefaults"]["exportDocumentTemplatePath"].as_str()
            })
            .unwrap_or("");
        view.set_rows(model(
            self.templates
                .iter()
                .enumerate()
                .map(|(index, template)| TemplateFileRow {
                    index: index as i32 + 1,
                    name: template.display_name.clone().into(),
                    path: template.template_path.clone().into(),
                    report_type: template.report_type.clone().into(),
                    builtin: template.template_path.starts_with("builtin:"),
                    default: template.template_path == default_path,
                })
                .collect(),
        ));
        view.set_can_edit(self.can(RENAME_REPORT_TEMPLATE));
        view.set_can_import(self.can(IMPORT_REPORT_TEMPLATE_FILE));
        view.set_can_export(self.can(DOWNLOAD_REPORT_TEMPLATE_FILE));
        view.set_can_delete(self.can(DELETE_REPORT_TEMPLATE));
    }
    pub fn template_files_open(&mut self, open: bool) {
        if let Some(ui) = self.ui.upgrade() {
            ui.global::<TemplateFiles>().set_open(open);
        }
        if open {
            self.refresh_template_files();
        }
    }
    /// Runs a file mutation directly when the revision is current, otherwise
    /// loads the template content first and defers the mutation.
    fn template_mutation(&mut self, operation: Operation, mut body: Value, reply: &str) {
        let Some(template) = self.selected_template() else {
            self.error("请先选择一个模板文件。");
            return;
        };
        let path = template.template_path;
        body["reportType"] = json!(self.report_type());
        body["templatePath"] = json!(path);
        if let Some((cached, revision)) = self.template_files.revision.clone() {
            if cached == path && !revision.is_empty() {
                body["expectedRevision"] = json!(revision);
                self.request(operation, 0, vec![], Some(body), reply);
                return;
            }
        }
        self.template_files.pending = Some(PendingTemplate::Request {
            operation,
            body,
            reply: reply.into(),
        });
        self.request(
            GET_REPORT_TEMPLATE_CONTENT,
            0,
            vec![("reportType", self.report_type()), ("templatePath", path)],
            None,
            "template-files:content",
        );
    }
    fn finish_pending_template(&mut self, revision: String) {
        let Some(pending) = self.template_files.pending.take() else {
            return;
        };
        match pending {
            PendingTemplate::Request {
                operation,
                mut body,
                reply,
            } => {
                body["expectedRevision"] = json!(revision);
                self.request(operation, 0, vec![], Some(body), reply);
            }
            PendingTemplate::Upload {
                mut metadata,
                source,
            } => {
                metadata["expectedRevision"] = json!(revision);
                self.start(Work::Upload {
                    operation: UPLOAD_REPORT_TEMPLATE_FILE,
                    parameters: vec![],
                    metadata,
                    source,
                    limit: FILE_LIMIT,
                    reply: "template-files:mutated".into(),
                });
            }
        }
    }
    pub fn template_file_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let view = ui.global::<TemplateFiles>();
        match action {
            "toggle" => self.template_files_open(!view.get_open()),
            "refresh" => self.refresh_template_files(),
            "select" => {}
            "rename" => {
                let new_name = view.get_new_name().trim().to_string();
                if new_name.is_empty() {
                    self.error("请输入新的模板文件名。");
                    return;
                }
                if !export_doc_engine::paths::valid_file_name(&format!("{new_name}.html")) {
                    self.error("模板文件名不符合跨平台规则或长度超过限制。");
                    return;
                }
                self.template_mutation(
                    RENAME_REPORT_TEMPLATE,
                    json!({ "newTemplatePath": new_name }),
                    "template-files:mutated",
                );
            }
            "set-default" => {
                let Some(template) = self.selected_template() else {
                    self.error("请先选择一个模板文件。");
                    return;
                };
                self.request(
                    SET_DEFAULT_REPORT_TEMPLATE,
                    0,
                    vec![],
                    Some(json!({
                        "reportType": self.report_type(),
                        "templatePath": template.template_path
                    })),
                    "template-files:mutated",
                );
            }
            "delete" => {
                let Some(template) = self.selected_template() else {
                    self.error("请先选择一个模板文件。");
                    return;
                };
                if template.template_path.starts_with("builtin:") {
                    self.error("内置模板为只读，不能删除。");
                    return;
                }
                self.confirm(
                    Pending::TemplateDelete(json!({
                        "reportType": self.report_type(),
                        "templatePath": template.template_path
                    })),
                    &format!(
                        "将删除模板文件“{}”，此操作不可撤销。",
                        template.display_name
                    ),
                );
            }
            "save-file" => {
                let Some(template) = self.selected_template() else {
                    self.error("请先选择一个模板文件。");
                    return;
                };
                let name = format!(
                    "{}.html",
                    template
                        .template_path
                        .rsplit(['/', '\\'])
                        .next()
                        .filter(|name| name.ends_with(".html"))
                        .map(|name| name.trim_end_matches(".html"))
                        .unwrap_or(&template.display_name)
                );
                let Some(destination) =
                    self.platform
                        .choose_destination(ui.window(), &name, &["html"])
                else {
                    self.status("已取消导出");
                    return;
                };
                self.request(
                    SAVE_REPORT_TEMPLATE_FILE_TO_PATH,
                    0,
                    vec![],
                    Some(json!({
                        "reportType": self.report_type(),
                        "templatePath": template.template_path,
                        "filePath": destination.to_string_lossy()
                    })),
                    "template-files:saved",
                );
            }
            "download-file" => {
                let Some(template) = self.selected_template() else {
                    self.error("请先选择一个模板文件。");
                    return;
                };
                let name = format!("{}.html", template.display_name);
                let Some(destination) =
                    self.platform
                        .choose_destination(ui.window(), &name, &["html"])
                else {
                    self.status("已取消下载");
                    return;
                };
                let body = json!({
                    "reportType": self.report_type(),
                    "templatePath": template.template_path
                });
                self.start(Work::BinarySave {
                    operation: DOWNLOAD_REPORT_TEMPLATE_FILE,
                    parameters: vec![],
                    query: vec![
                        ("reportType", self.report_type()),
                        ("templatePath", template.template_path),
                    ],
                    body: Some(body),
                    destination,
                    limit: FILE_LIMIT,
                });
            }
            "upload-file" => {
                let Some(template) = self.selected_template() else {
                    self.error("请先选择一个模板文件。");
                    return;
                };
                if template.template_path.starts_with("builtin:") {
                    self.error("内置模板为只读，请选择用户模板后再上传。");
                    return;
                }
                let Some(source) =
                    self.platform
                        .choose_source(ui.window(), "HTML 模板文件", &["html"])
                else {
                    self.status("已取消上传");
                    return;
                };
                let metadata = json!({
                    "reportType": self.report_type(),
                    "templatePath": template.template_path
                });
                if let Some((cached, revision)) = self.template_files.revision.clone() {
                    if cached == template.template_path && !revision.is_empty() {
                        let mut metadata = metadata;
                        metadata["expectedRevision"] = json!(revision);
                        self.start(Work::Upload {
                            operation: UPLOAD_REPORT_TEMPLATE_FILE,
                            parameters: vec![],
                            metadata,
                            source,
                            limit: FILE_LIMIT,
                            reply: "template-files:mutated".into(),
                        });
                        return;
                    }
                }
                self.template_files.pending = Some(PendingTemplate::Upload { metadata, source });
                self.request(
                    GET_REPORT_TEMPLATE_CONTENT,
                    0,
                    vec![
                        ("reportType", self.report_type()),
                        ("templatePath", template.template_path),
                    ],
                    None,
                    "template-files:content",
                );
            }
            "import-file" => {
                let Some(template) = self.selected_template() else {
                    self.error("请先选择一个模板文件。");
                    return;
                };
                let Some(source) =
                    self.platform
                        .choose_source(ui.window(), "HTML 模板文件", &["html"])
                else {
                    self.status("已取消导入");
                    return;
                };
                self.template_mutation(
                    IMPORT_REPORT_TEMPLATE_FILE,
                    json!({ "filePath": source.to_string_lossy() }),
                    "template-files:mutated",
                );
            }
            "download-package" => {
                let Some(destination) = self.platform.choose_destination(
                    ui.window(),
                    "report-templates.edtpl",
                    &["edtpl"],
                ) else {
                    self.status("已取消导出");
                    return;
                };
                self.start(Work::BinarySave {
                    operation: DOWNLOAD_REPORT_TEMPLATE_PACKAGE,
                    parameters: vec![],
                    query: vec![],
                    body: None,
                    destination,
                    limit: PACKAGE_LIMIT,
                });
            }
            "import-package" => {
                let Some(source) =
                    self.platform
                        .choose_source(ui.window(), "模板包", &["edtpl", "zip"])
                else {
                    self.status("已取消导入");
                    return;
                };
                self.request(
                    IMPORT_REPORT_TEMPLATE_PACKAGE,
                    0,
                    vec![],
                    Some(json!({
                        "packagePath": source.to_string_lossy(),
                        "strategy": "SkipExisting"
                    })),
                    "template-files:package-imported",
                );
            }
            _ => {}
        }
    }
    pub fn template_files_loaded(&mut self, reply: &str, value: Value) {
        match reply {
            "template-files:storage" => {
                match serde_json::from_value::<ApiReportTemplateStorageStatusResponse>(value) {
                    Ok(status) => {
                        if let Some(ui) = self.ui.upgrade() {
                            let view = ui.global::<TemplateFiles>();
                            view.set_loaded(true);
                            view.set_template_root(status.template_root.into());
                            view.set_storage_exists(status.exists);
                            view.set_storage_writable(status.writable);
                            view.set_storage_message(status.message.into());
                            view.set_storage_policy(status.storage_policy.into());
                        }
                        self.request(
                            LIST_REPORT_TEMPLATES,
                            0,
                            vec![("reportType", self.report_type())],
                            None,
                            "template-files:list",
                        );
                    }
                    Err(cause) => self.error(cause.to_string()),
                }
            }
            "template-files:list" => self.templates_loaded(value),
            "template-files:content" => {
                match serde_json::from_value::<ApiReportTemplateContentDto>(value) {
                    Ok(content) => {
                        self.template_files.revision =
                            Some((content.template_path, content.revision));
                        self.finish_pending_template(content.revision);
                    }
                    Err(cause) => {
                        self.template_files.pending = None;
                        self.error(cause.to_string());
                    }
                }
            }
            "template-files:mutated" => {
                self.template_files.revision = None;
                let message = value["message"].as_str().unwrap_or("模板文件已更新");
                self.refresh_template_files();
                self.status(message);
            }
            "template-files:saved" => {
                match serde_json::from_value::<ApiReportTemplateFileExportResponse>(value) {
                    Ok(response) => self.status(format!(
                        "已导出模板文件：{}（{} 字节）",
                        response.file_path, response.bytes
                    )),
                    Err(cause) => self.error(cause.to_string()),
                }
            }
            "template-files:package-imported" => {
                match serde_json::from_value::<ApiReportTemplatePackageImportResponse>(value) {
                    Ok(response) => {
                        self.refresh_template_files();
                        self.status(format!("已导入 {} 个模板。", response.template_count));
                    }
                    Err(cause) => self.error(cause.to_string()),
                }
            }
            _ => {}
        }
    }
}
