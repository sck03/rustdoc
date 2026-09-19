use super::*;
use export_doc_engine::pdf::PdfPageImage;

impl Desktop {
    pub fn clear_report(&mut self) {
        self.pdf = None;
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_pdf_pages(0);
            app.set_pdf_page(0);
            app.set_pdf_image(Default::default());
        }
    }

    pub fn open_payment_report(&mut self) {
        if self.task.is_some() || self.workspace.resource != "payments" {
            return;
        }
        if !self.can(PREVIEW_PAYMENT_VOUCHER_HTML) {
            self.error("当前账号没有输出付款报销单的权限。");
            return;
        }
        let Some(record) = &self.workspace.selected else {
            return;
        };
        let id = record["id"].as_i64().unwrap_or(0);
        if id <= 0 {
            return;
        }
        let name = record["voucherNo"]
            .as_str()
            .filter(|value| !value.is_empty())
            .unwrap_or("付款报销单")
            .to_owned();
        self.report_payment = Some((id, name.clone()));
        self.clear_report();
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_page("payment-report".into());
            app.set_title(format!("付款报销 · {name}").into());
            app.set_description("选择付款单或费用报销明细单，预览后导出 PDF".into());
            app.set_report_type("PaymentVoucher".into());
            app.set_report_ready(true);
            app.set_report_export_ready(self.can(START_PAYMENT_VOUCHER_PDF_SAVE_TO_PATH_JOB));
            app.set_report_with_seal(false);
        }
        self.request(
            LIST_REPORT_TEMPLATES,
            0,
            vec![("reportType", "PaymentVoucher".into())],
            None,
            "templates",
        );
    }

    pub fn pdf_action(&mut self, action: &str) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        if self.task.is_some() && action != "cancel" {
            return;
        }
        match action {
            "preview" => {
                let Some(template) = self
                    .templates
                    .get(app.get_report_template_index().max(0) as usize)
                else {
                    self.error("请选择报表模板。");
                    return;
                };
                let template = template.template_path.clone();
                let (operation, record_id, body) = if self.payment_form.is_some() {
                    let draft = match self.payment_draft() {
                        Ok(draft) => draft,
                        Err(cause) => {
                            self.error(cause);
                            return;
                        }
                    };
                    (
                        PREVIEW_PAYMENT_VOUCHER_DRAFT_HTML,
                        draft.id,
                        serde_json::json!({"templatePath":template,"payment":draft}),
                    )
                } else if let Some((id, _)) = &self.report_payment {
                    (
                        PREVIEW_PAYMENT_VOUCHER_HTML,
                        *id,
                        serde_json::json!({"templatePath":template}),
                    )
                } else {
                    if let Err(cause) = self.invoice_form.validate() {
                        self.error(cause);
                        return;
                    }
                    let draft = match self.grid.draft.build() {
                        Ok(draft) => draft,
                        Err(cause) => {
                            self.error(cause);
                            return;
                        }
                    };
                    (
                        PREVIEW_INVOICE_REPORT_DRAFT_HTML,
                        draft.id,
                        serde_json::json!({"templatePath":template,"invoice":draft,"withSeal":app.get_report_with_seal()}),
                    )
                };
                if !self.can(operation) {
                    self.error("当前账号没有预览此单据的权限。");
                    return;
                }
                self.clear_report();
                self.start(Work::PreviewReport {
                    operation,
                    record_id,
                    body,
                });
            }
            "invalidate" => self.clear_report(),
            "back" => {
                self.clear_report();
                self.report_payment = None;
                self.navigate_now("payments");
            }
            "save" => {
                if self.pdf.is_some() {
                    let (operation, id, dirty) = if let Some((id, _)) = &self.report_payment {
                        (
                            START_PAYMENT_VOUCHER_PDF_SAVE_TO_PATH_JOB,
                            *id,
                            self.payment_dirty(),
                        )
                    } else {
                        (
                            START_INVOICE_REPORT_PDF_SAVE_TO_PATH_JOB,
                            self.grid.draft.header.id,
                            self.grid.dirty() || !self.invoice_form.error.is_empty(),
                        )
                    };
                    if id <= 0 || dirty || !self.can(operation) {
                        self.error("请先保存单据，并确认当前账号具有 PDF 导出权限。");
                        return;
                    }
                    let number = self
                        .report_payment
                        .as_ref()
                        .map(|(_, number)| number.as_str())
                        .unwrap_or(&self.grid.draft.header.invoice_no);
                    let Some(template) = self
                        .templates
                        .get(app.get_report_template_index().max(0) as usize)
                    else {
                        self.error("请选择报表模板。");
                        return;
                    };
                    let label = &template.display_name;
                    let name =
                        export_doc_engine::paths::suggested_pdf_name(&format!("{number}-{label}"));
                    if let Some(destination) =
                        self.platform.choose_pdf_destination(ui.window(), &name)
                    {
                        let body = serde_json::json!({"templatePath":template.template_path,"withSeal":self.report_payment.is_none()&&app.get_report_with_seal(),"destinationPath":destination});
                        self.start(Work::FileJob {
                            operation,
                            parameters: crate::worker::parameters(operation, id),
                            body,
                            destination,
                        });
                    } else {
                        self.status("已取消保存");
                    }
                }
            }
            "next" | "previous" => {
                if let Some(bytes) = self.pdf.clone() {
                    let index = (app.get_pdf_page() + if action == "next" { 1 } else { -1 })
                        .clamp(0, (app.get_pdf_pages() - 1).max(0));
                    self.start(Work::Page {
                        bytes,
                        index: index as u32,
                    });
                }
            }
            "cancel" => self.cancel_operation(),
            _ => {}
        }
    }

    pub fn show_pdf(&self, page: PdfPageImage) {
        match image::load_from_memory(&page.png) {
            Ok(image) => {
                let image = image.to_rgba8();
                let buffer = slint::SharedPixelBuffer::<slint::Rgba8Pixel>::clone_from_slice(
                    image.as_raw(),
                    image.width(),
                    image.height(),
                );
                if let Some(ui) = self.ui.upgrade() {
                    let app = ui.global::<App>();
                    app.set_pdf_aspect_ratio(image.width() as f32 / image.height().max(1) as f32);
                    app.set_pdf_image(slint::Image::from_rgba8(buffer));
                    app.set_pdf_page(page.page_index as i32);
                    app.set_pdf_pages(page.page_count as i32);
                }
            }
            Err(error) => self.error(format!("无法显示 PDF 页面：{error}")),
        }
    }
}
