use super::*;
use crate::{AttachmentRow, AttachmentVersion, PageTab, attachment_model::file_size};
use export_doc_engine::{contracts, workspace};
use serde_json::{Value, json};

impl Desktop {
    pub fn refresh_attachments(&mut self) {
        if let Some(details) = &self.attachments.details {
            self.request(
                GET_BUSINESS_ATTACHMENT,
                details.attachment.id,
                vec![],
                None,
                "attachment-detail",
            );
            return;
        }
        let search = self
            .ui
            .upgrade()
            .map(|ui| ui.global::<App>().get_search().to_string())
            .unwrap_or_default();
        let mut query = vec![
            ("pageNumber", self.workspace.page.max(1).to_string()),
            ("pageSize", "30".into()),
            ("keyword", search),
            (
                "includeArchived",
                self.attachments.include_archived.to_string(),
            ),
        ];
        if let Some(id) = self.attachments.invoice_id {
            query.push(("invoiceId", id.to_string()));
        }
        if let Some(id) = self.attachments.category_id {
            query.push(("categoryId", id.to_string()));
        }
        self.start(Work::Attachments { query });
    }
    pub fn attachments_loaded(&mut self, value: Value) {
        let catalog = match serde_json::from_value::<BusinessAttachmentCategoryCatalog>(
            value["categories"].clone(),
        ) {
            Ok(value) => value,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        let page = match serde_json::from_value::<BusinessAttachmentPage>(value["page"].clone()) {
            Ok(value) => value,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        self.attachments.categories = catalog.items;
        self.attachments.company = catalog.company_scope;
        self.attachments.invoices = value["invoices"].as_array().cloned().unwrap_or_default();
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        app.set_attachment_can_upload(page.can_upload);
        app.set_attachment_can_manage_categories(catalog.can_manage);
        app.set_attachment_invoice_options(model(
            std::iter::once("全部发票 · 上传前请选择单据".into())
                .chain(self.attachments.invoices.iter().map(|invoice| {
                    format!(
                        "{} · {} · {}",
                        workspace::display(&invoice["invoiceNo"]),
                        workspace::display(&invoice["type"]),
                        workspace::display(&invoice["customerNameEN"])
                    )
                    .into()
                }))
                .collect(),
        ));
        app.set_attachment_category_options(model(
            std::iter::once("全部分类".into())
                .chain(
                    self.attachments
                        .categories
                        .iter()
                        .map(|category| category.name.clone().into()),
                )
                .collect(),
        ));
        app.set_attachment_invoice_index(
            self.attachments
                .invoice_id
                .and_then(|id| {
                    self.attachments
                        .invoices
                        .iter()
                        .position(|invoice| invoice["id"] == id)
                })
                .map(|index| index as i32 + 1)
                .unwrap_or(0),
        );
        app.set_attachment_category_index(
            self.attachments
                .category_id
                .and_then(|id| {
                    self.attachments
                        .categories
                        .iter()
                        .position(|category| category.id == id)
                })
                .map(|index| index as i32 + 1)
                .unwrap_or(0),
        );
        app.set_attachment_rows(model(
            page.page
                .items
                .iter()
                .map(|item| AttachmentRow {
                    id: item.id as i32,
                    title: item.title.clone().into(),
                    invoice: item.invoice_no.clone().into(),
                    customer: item.customer_name.clone().into(),
                    category: item.category_name.clone().into(),
                    reference: format!("PO {} · 款号 {}", item.po_number, item.style_no).into(),
                    version: format!(
                        "最新 v{} · {}",
                        item.latest_revision,
                        item.current_revision
                            .map(|revision| format!("有效 v{revision}"))
                            .unwrap_or_else(|| "尚未确认有效版本".into())
                    )
                    .into(),
                    archived: item.is_archived,
                })
                .collect(),
        ));
        app.set_attachment_capacity(
            format!(
                "单个文件 {} · 每张发票 {}{}",
                file_size(page.file_bytes_limit),
                file_size(page.invoice_bytes_limit),
                page.used_bytes
                    .map(|used| format!(" · 已用 {}", file_size(used)))
                    .unwrap_or_default()
            )
            .into(),
        );
        app.set_list_page(page.page.page_number as i32);
        app.set_total_pages(page.page.total_pages as i32);
        app.set_total_records(page.page.total_count as i32);
        app.set_attachment_detail(false);
        app.set_attachment_preview_kind("".into());
    }
    pub fn attachment_detail(&mut self, value: Value) {
        let details = match serde_json::from_value::<BusinessAttachmentDetails>(value) {
            Ok(value) => value,
            Err(cause) => {
                self.error(format!("资料版本格式无效：{cause}"));
                return;
            }
        };
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let item = &details.attachment;
        app.set_attachment_detail(true);
        app.set_attachment_title(item.title.clone().into());
        app.set_attachment_description(
            format!(
                "{} · {} · {} · PO {} · 款号 {}",
                item.invoice_no,
                item.invoice_type,
                item.category_name,
                item.po_number,
                item.style_no
            )
            .into(),
        );
        app.set_attachment_can_edit(item.can_edit);
        app.set_attachment_can_delete(item.can_delete);
        app.set_attachment_archived(item.is_archived);
        app.set_attachment_versions(model(
            details
                .revisions
                .iter()
                .rev()
                .map(|version| AttachmentVersion {
                    revision: version.revision as i32,
                    file_name: version.file_name.clone().into(),
                    size: file_size(version.length).into(),
                    uploaded: format!("{} · {}", version.uploaded_by, version.created_at).into(),
                    note: version.note.clone().into(),
                    current: item.current_revision == Some(version.revision),
                    can_preview: version.content_type == "application/pdf"
                        || version.content_type.starts_with("image/")
                        || version.content_type.starts_with("text/"),
                })
                .collect(),
        ));
        app.set_attachment_events(model(
            details
                .events
                .iter()
                .rev()
                .map(|event| PageTab {
                    key: event.action.clone().into(),
                    label: format!(
                        "{} · {} · {}{}\n{}",
                        event.created_at,
                        event.actor_name,
                        match event.action.as_str() {
                            "Archive" => "停用",
                            "Restore" => "恢复",
                            "Edit" => "修正资料",
                            _ => "确认版本",
                        },
                        event
                            .revision
                            .map(|revision| format!(" v{revision}"))
                            .unwrap_or_default(),
                        event.note
                    )
                    .into(),
                })
                .collect(),
        ));
        self.attachments.details = Some(details);
    }
    pub fn attachment_action(&mut self, action: &str, value: &str) {
        if self.task.is_some() {
            return;
        }
        match action {
            "filter" => {
                let Some(ui) = self.ui.upgrade() else {
                    return;
                };
                let app = ui.global::<App>();
                let invoice_id = app
                    .get_attachment_invoice_index()
                    .checked_sub(1)
                    .and_then(|index| self.attachments.invoices.get(index as usize))
                    .and_then(|invoice| invoice["id"].as_i64());
                if invoice_id != self.attachments.invoice_id {
                    app.set_attachment_category_index(0);
                }
                self.attachments.invoice_id = invoice_id;
                self.attachments.category_id = app
                    .get_attachment_category_index()
                    .checked_sub(1)
                    .and_then(|index| self.attachments.categories.get(index as usize))
                    .map(|category| category.id);
                self.attachments.include_archived = app.get_attachment_include_archived();
                self.workspace.page = 1;
                self.refresh_attachments();
            }
            "open" => {
                if let Ok(id) = value.parse::<i64>() {
                    self.attachments.preview_kind.clear();
                    if let Some(ui) = self.ui.upgrade() {
                        let app = ui.global::<App>();
                        app.set_attachment_note("".into());
                        app.set_attachment_preview_kind("".into());
                    }
                    self.request(
                        GET_BUSINESS_ATTACHMENT,
                        id,
                        vec![],
                        None,
                        "attachment-detail",
                    );
                }
            }
            "back" => {
                self.attachments.details = None;
                self.attachments.preview_kind.clear();
                self.clear_report();
                self.refresh_attachments();
            }
            "invoice" => {
                if let Some(details) = &self.attachments.details {
                    self.pending_open = Some(("invoices", details.attachment.invoice_id));
                    self.navigate_now("invoices");
                }
            }
            "upload" | "replace" => self.open_attachment_upload(action == "replace"),
            "category" => self.edit_attachment_category(false),
            "category-rename" => self.edit_attachment_category(true),
            "category-delete" => self.delete_attachment_category(),
            "edit" => self.edit_attachment_metadata(),
            "archive" | "confirm" | "delete" => self.confirm_attachment(action, value),
            "download" | "preview" => {
                if let Ok(revision) = value.parse::<i64>() {
                    self.read_attachment(revision, action == "preview");
                }
            }
            "close-preview" => {
                self.attachments.preview_kind.clear();
                self.clear_report();
                if let Some(ui) = self.ui.upgrade() {
                    let app = ui.global::<App>();
                    app.set_attachment_preview_kind("".into());
                    app.set_attachment_image(Default::default());
                    app.set_attachment_text("".into());
                }
            }
            _ => {}
        }
    }
    fn category_lookups(&self) -> Lookups {
        let mut lookups = self.lookups.clone();
        lookups.insert(
            "categoryId".into(),
            self.attachments
                .categories
                .iter()
                .map(|category| (json!(category.id), category.name.clone()))
                .collect(),
        );
        lookups
    }
    fn open_attachment_upload(&mut self, replace: bool) {
        if !self.can(UPLOAD_BUSINESS_ATTACHMENT) {
            self.error("当前账号没有上传资料的权限。");
            return;
        }
        let item = if replace {
            self.attachments
                .details
                .as_ref()
                .map(|details| &details.attachment)
        } else {
            None
        };
        let invoice_id = item
            .map(|item| item.invoice_id)
            .or(self.attachments.invoice_id);
        let Some(invoice_id) = invoice_id else {
            self.error("请先选择资料所属的发票。");
            return;
        };
        if self.attachments.categories.is_empty() {
            self.error("请先新增资料分类。");
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let Some(source) = self.platform.choose_source(
            ui.window(),
            "业务资料",
            &[
                "pdf", "png", "jpg", "jpeg", "gif", "webp", "xls", "xlsx", "doc", "docx", "pptx",
                "txt", "csv",
            ],
        ) else {
            return;
        };
        let mut value = contracts::initial(contracts::schema("ApiAttachmentUploadForm"));
        value["title"] = json!(
            source
                .file_stem()
                .and_then(|name| name.to_str())
                .unwrap_or("业务资料")
        );
        value["categoryId"] = json!(self.attachments.categories[0].id);
        value["poNumber"] = json!("");
        value["styleNo"] = json!("");
        value["note"] = json!("");
        if let Some(item) = item {
            value = contracts::overlay(value, &json!(item));
            value["attachmentId"] = json!(item.id);
            value["expectedVersion"] = json!(item.version_number);
        }
        value["uploadKey"] = match export_doc_engine::paths::nonce() {
            Ok(key) => json!(key),
            Err(cause) => {
                self.error(cause);
                return;
            }
        };
        let mut schema = contracts::resolve(contracts::schema("ApiAttachmentUploadForm")).clone();
        for key in ["file", "attachmentId", "uploadKey"] {
            schema["properties"][key]["readOnly"] = json!(true);
        }
        if replace {
            for key in ["title", "categoryId", "poNumber", "styleNo"] {
                schema["properties"][key]["readOnly"] = json!(true);
            }
        }
        let title = format!(
            "{} · {}",
            if replace {
                "上传新版本"
            } else {
                "上传业务资料"
            },
            source
                .file_name()
                .and_then(|name| name.to_str())
                .unwrap_or("")
        );
        self.attachments.source = Some(source);
        self.form_id = invoice_id;
        self.form_operation = Some(UPLOAD_BUSINESS_ATTACHMENT);
        self.form_reply = "attachment-uploaded".into();
        self.form = Some(FormModel::new(&schema, value, self.category_lookups()));
        let app = ui.global::<App>();
        app.set_form_title(title.into());
        app.set_form_open(true);
        self.sync_form();
    }
    pub fn save_attachment_upload(&mut self, metadata: Value) {
        let Some(source) = self.attachments.source.clone() else {
            self.error("请选择要上传的文件。");
            return;
        };
        self.start(Work::Upload {
            operation: UPLOAD_BUSINESS_ATTACHMENT,
            parameters: vec![("invoiceId", self.form_id.to_string())],
            metadata,
            source,
            limit: 16 * 1024 * 1024,
            reply: "attachment-uploaded".into(),
        });
    }
    fn edit_attachment_category(&mut self, editing: bool) {
        let operation = if editing {
            UPDATE_BUSINESS_ATTACHMENT_CATEGORY
        } else {
            CREATE_BUSINESS_ATTACHMENT_CATEGORY
        };
        if !self.can(operation) {
            return;
        }
        let mut body = contracts::object(operation.id, true);
        body["companyScope"] = json!(self.attachments.company);
        self.form_id = 0;
        if editing {
            let Some(category) = self
                .attachments
                .categories
                .iter()
                .find(|row| Some(row.id) == self.attachments.category_id)
            else {
                self.error("请先选择要改名的分类。");
                return;
            };
            body["name"] = json!(category.name);
            body["expectedVersion"] = json!(category.version_number);
            self.form_id = category.id;
        }
        let mut schema = contracts::resolve(contracts::request(operation.id)).clone();
        schema["properties"]["companyScope"]["readOnly"] = json!(true);
        self.form = Some(FormModel::new(&schema, body, Lookups::new()));
        self.form_operation = Some(operation);
        self.form_reply = "attachment-category-saved".into();
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_form_title(
                if editing {
                    "重命名资料分类"
                } else {
                    "新增资料分类"
                }
                .into(),
            );
            app.set_form_open(true);
        }
        self.sync_form();
    }
    fn delete_attachment_category(&mut self) {
        if !self.can(DELETE_BUSINESS_ATTACHMENT_CATEGORY) {
            return;
        }
        let Some(category) = self
            .attachments
            .categories
            .iter()
            .find(|row| Some(row.id) == self.attachments.category_id)
        else {
            self.error("请先选择要删除的分类。");
            return;
        };
        if category.attachment_count > 0 {
            self.error("该分类仍有资料使用（含停用资料），不能删除。");
            return;
        }
        self.confirm(
            Pending::AttachmentCategoryDelete(category.id, category.version_number),
            &format!("确认删除未使用的分类“{}”？", category.name),
        );
    }
    fn edit_attachment_metadata(&mut self) {
        let Some(details) = &self.attachments.details else {
            return;
        };
        if !details.attachment.can_edit {
            return;
        }
        let mut value = contracts::overlay(
            contracts::object(EDIT_BUSINESS_ATTACHMENT_METADATA.id, true),
            &json!(details.attachment),
        );
        value["expectedVersion"] = json!(details.attachment.version_number);
        self.form_id = details.attachment.id;
        self.form_operation = Some(EDIT_BUSINESS_ATTACHMENT_METADATA);
        self.form_reply = "attachment-saved".into();
        self.form = Some(FormModel::new(
            contracts::request(EDIT_BUSINESS_ATTACHMENT_METADATA.id),
            value,
            self.category_lookups(),
        ));
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_form_title("修正业务资料信息".into());
            app.set_form_open(true);
        }
        self.sync_form();
    }
    fn confirm_attachment(&mut self, action: &str, value: &str) {
        let (Some(details), Some(ui)) = (&self.attachments.details, self.ui.upgrade()) else {
            return;
        };
        let item = &details.attachment;
        let note = ui.global::<App>().get_attachment_note().to_string();
        if note.trim().is_empty() {
            self.error("请填写本次操作的依据或原因。");
            return;
        }
        let operation = if action == "delete" {
            DELETE_BUSINESS_ATTACHMENT
        } else {
            UPDATE_BUSINESS_ATTACHMENT
        };
        if !self.can(operation) {
            self.error("当前账号没有此项操作权限。");
            return;
        }
        let revision = if action == "confirm" {
            value.parse::<i64>().ok()
        } else {
            item.current_revision
        };
        let body = json!({"expectedVersion":item.version_number,"note":note,"currentRevision":revision,"isArchived":if action=="archive"{!item.is_archived}else{item.is_archived}});
        self.confirm(
            Pending::AttachmentMutation(operation, body),
            if action == "delete" {
                "确认永久删除该资料的全部文件版本？"
            } else if action == "archive" {
                if item.is_archived {
                    "确认恢复使用该资料？"
                } else {
                    "确认停用该资料？文件及历史版本仍会保留。"
                }
            } else {
                "确认将所选版本设为正式有效版本？"
            },
        );
    }
    pub fn mutate_attachment(&mut self, operation: Operation, body: Value) {
        if let Some(details) = &self.attachments.details {
            self.request(
                operation,
                details.attachment.id,
                vec![],
                Some(body),
                if operation == DELETE_BUSINESS_ATTACHMENT {
                    "attachment-deleted"
                } else {
                    "attachment-saved"
                },
            );
        }
    }
    fn read_attachment(&mut self, revision: i64, preview: bool) {
        let (Some(details), Some(ui)) = (&self.attachments.details, self.ui.upgrade()) else {
            return;
        };
        let Some(version) = details
            .revisions
            .iter()
            .find(|version| version.revision == revision)
        else {
            return;
        };
        let parameters = vec![
            ("id", details.attachment.id.to_string()),
            ("revision", revision.to_string()),
        ];
        if preview {
            let kind = if version.content_type == "application/pdf" {
                "pdf"
            } else if version.content_type.starts_with("image/") {
                "image"
            } else if version.content_type.starts_with("text/") {
                "text"
            } else {
                self.error("请保存原文件后使用对应软件查看此格式。");
                return;
            };
            let content_type = version.content_type.clone();
            let name = version.file_name.clone();
            self.attachments.preview_kind = kind.into();
            self.clear_report();
            let app = ui.global::<App>();
            app.set_attachment_preview_kind(kind.into());
            app.set_attachment_preview_title(name.into());
            app.set_attachment_image(Default::default());
            app.set_attachment_text("".into());
            self.start(Work::AttachmentPreview {
                parameters,
                content_type,
            });
        } else {
            let extension = std::path::Path::new(&version.file_name)
                .extension()
                .and_then(|extension| extension.to_str())
                .unwrap_or("");
            if let Some(destination) =
                self.platform
                    .choose_destination(ui.window(), &version.file_name, &[extension])
            {
                self.start(Work::BinarySave {
                    operation: DOWNLOAD_BUSINESS_ATTACHMENT,
                    parameters,
                    query: vec![],
                    destination,
                    limit: 16 * 1024 * 1024,
                });
            }
        }
    }
}
