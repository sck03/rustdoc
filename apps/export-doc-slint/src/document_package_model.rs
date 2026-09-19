//! Invoice document-package selection and output options.
use export_doc_engine::generated_api::ApiReportTemplateDto;

#[derive(Clone, Default)]
pub struct DocumentPackageTemplate {
    pub name: String,
    pub template_path: String,
    pub with_seal: bool,
    pub selected: bool,
}

#[derive(Clone, Default)]
pub struct DocumentPackageModel {
    pub templates: Vec<DocumentPackageTemplate>,
    pub include_merged_pdf: bool,
    pub preview_ready: bool,
}

impl DocumentPackageModel {
    pub fn load(&mut self, templates: &[ApiReportTemplateDto]) {
        self.templates = templates
            .iter()
            .filter(|template| template.report_type == "ExportDocument")
            .map(|template| DocumentPackageTemplate {
                name: template.display_name.clone(),
                template_path: template.template_path.clone(),
                with_seal: template.with_seal_default.unwrap_or(false),
                selected: true,
            })
            .collect();
        self.include_merged_pdf = self.templates.len() > 1;
        self.preview_ready = !self.templates.is_empty();
    }

    pub fn selected_items(&self) -> Vec<serde_json::Value> {
        self.templates
            .iter()
            .filter(|template| template.selected)
            .map(|template| {
                serde_json::json!({
                    "reportType": "ExportDocument",
                    "templatePath": template.template_path,
                    "name": template.name,
                    "withSeal": template.with_seal
                })
            })
            .collect()
    }

    pub fn selected_count(&self) -> usize {
        self.templates
            .iter()
            .filter(|template| template.selected)
            .count()
    }
}
