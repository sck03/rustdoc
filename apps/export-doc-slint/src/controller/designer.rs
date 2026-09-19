use super::*;
use crate::{DesignElement, FormField};
use export_doc_engine::{
    designer::{Field, Kind},
    template,
};
use serde_json::json;

impl Desktop {
    pub fn open_designer(&mut self, template: Option<ApiUserReportTemplateDto>) {
        let design = match &template {
            Some(t) => match Design::from_html(&t.content_html) {
                Ok(d) => d,
                Err(e) => {
                    self.error(e);
                    return;
                }
            },
            None => Design::invoice(),
        };
        self.design = design;
        self.template = template;
        self.selected_element = "title".into();
        self.design_history.clear();
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_page("designer".into());
            app.set_title("报表设计".into());
            app.set_template_name(
                self.template
                    .as_ref()
                    .map(|t| t.name.clone())
                    .unwrap_or_else(|| "自定义商业发票".into())
                    .into(),
            );
            app.set_template_saved(self.template.is_some());
        }
        self.sync_design();
        self.sync_design_fields();
        if self.bindings.is_empty() {
            self.request(
                GET_REPORT_TEMPLATE_FIELD_CATALOG,
                0,
                vec![("reportType", "ExportDocument".into())],
                None,
                "field-catalog",
            );
        }
    }
    pub fn sync_design(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let rows: Vec<DesignElement> = self
            .design
            .layers
            .iter()
            .filter(|l| l.visible)
            .flat_map(|l| &l.elements)
            .filter(|e| e.visible)
            .map(|element| {
                let text = match &element.kind {
                    Kind::Text { text } => text.clone(),
                    Kind::Field { field_path, .. } => format!("{{{field_path}}}"),
                    Kind::Flow { block, .. } => format!(
                        "{}\n\n商品明细 · 随行数自动分页",
                        block
                            .columns
                            .iter()
                            .map(|c| c.title.as_str())
                            .collect::<Vec<_>>()
                            .join("  |  ")
                    ),
                    _ => String::new(),
                };
                DesignElement {
                    id: element.id.clone().into(),
                    label: element.label.clone().into(),
                    text: text.into(),
                    x: element.x_hundredth_mm as f32,
                    y: element.y_hundredth_mm as f32,
                    w: element.width_hundredth_mm as f32,
                    h: element.height_hundredth_mm as f32,
                    selected: element.id == self.selected_element,
                    kind: element.kind.name().into(),
                    font_size: element.style.font_size_pt,
                }
            })
            .collect();
        let current = app.get_design_elements();
        if current.row_count() == rows.len() {
            if let Some(values) = current
                .as_any()
                .downcast_ref::<slint::VecModel<DesignElement>>()
            {
                for (i, row) in rows.into_iter().enumerate() {
                    values.set_row_data(i, row);
                }
                return;
            }
        }
        app.set_design_elements(model(rows));
    }
    pub fn sync_design_fields(&self) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let Some(e) = self.design.element(&self.selected_element) else {
            ui.global::<App>().set_design_fields(model(vec![]));
            return;
        };
        let mut fields = vec![
            ("label", "元素名称", e.label.clone()),
            (
                "x",
                "横向位置 (mm)",
                format!("{}", e.x_hundredth_mm as f32 / 100.),
            ),
            (
                "y",
                "纵向位置 (mm)",
                format!("{}", e.y_hundredth_mm as f32 / 100.),
            ),
            (
                "width",
                "宽度 (mm)",
                format!("{}", e.width_hundredth_mm as f32 / 100.),
            ),
            (
                "height",
                "高度 (mm)",
                format!("{}", e.height_hundredth_mm as f32 / 100.),
            ),
            ("fontSize", "字号 (pt)", e.style.font_size_pt.to_string()),
            ("color", "文字颜色", e.style.color.clone()),
        ];
        match &e.kind {
            Kind::Text { text } => fields.push(("text", "文字内容", text.clone())),
            Kind::Field { field_path, .. } => {
                fields.push(("fieldPath", "绑定字段", field_path.clone()))
            }
            _ => {}
        }
        ui.global::<App>().set_design_fields(model(
            fields
                .into_iter()
                .map(|(key, label, value)| FormField {
                    key: key.into(),
                    label: label.into(),
                    value: value.into(),
                    kind: if key == "text" { "multiline" } else { "text" }.into(),
                    ..Default::default()
                })
                .collect(),
        ));
    }
    pub fn designer_select(&mut self, id: &str) {
        self.selected_element = id.into();
        self.design_history.end_group();
        self.sync_design();
        self.sync_design_fields();
    }
    pub fn designer_move(&mut self, id: &str, dx: f32, dy: f32) {
        let before = self.design.clone();
        self.design.move_selection(
            &BTreeSet::from([id.to_string()]),
            dx.round() as i32,
            dy.round() as i32,
        );
        self.design_history.record(before, &self.design, None);
        self.sync_design();
        self.sync_design_fields();
    }
    pub fn designer_field(&mut self, key: &str, text: &str) {
        let before = self.design.clone();
        let Some(e) = self.design.element_mut(&self.selected_element) else {
            return;
        };
        let result: Result<(), String> = (|| {
            let number = || {
                text.parse::<f32>()
                    .ok()
                    .filter(|n| n.is_finite() && *n >= 0.)
                    .ok_or_else(|| "请输入非负数字。".to_string())
            };
            match key {
                "label" => e.label = text.into(),
                "text" => {
                    if let Kind::Text { text: current } = &mut e.kind {
                        *current = text.into();
                    }
                }
                "fieldPath" => {
                    if let Kind::Field { field_path, .. } = &mut e.kind {
                        *field_path = text.into();
                    }
                }
                "x" => e.x_hundredth_mm = (number()? * 100.).round() as i32,
                "y" => e.y_hundredth_mm = (number()? * 100.).round() as i32,
                "width" => e.width_hundredth_mm = (number()? * 100.).round() as i32,
                "height" => e.height_hundredth_mm = (number()? * 100.).round() as i32,
                "fontSize" => e.style.font_size_pt = number()?,
                "color" => {
                    if text.len() != 7
                        || !text.starts_with('#')
                        || !text[1..].bytes().all(|c| c.is_ascii_hexdigit())
                    {
                        return Err("颜色请使用 #RRGGBB。".into());
                    }
                    e.style.color = text.into();
                }
                _ => {}
            }
            Ok(())
        })();
        if let Err(error) = result {
            self.design = before;
            self.error(error);
            return;
        }
        self.design_history.record(
            before,
            &self.design,
            Some(format!("element:{}:{key}", self.selected_element)),
        );
        self.sync_design();
    }
    pub fn designer_action(&mut self, action: &str) {
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        let before = self.design.clone();
        match action {
            "save" => {
                let fields: Vec<Field> = self
                    .bindings
                    .iter()
                    .map(|(path, label)| Field {
                        path: path.clone(),
                        label: label.clone(),
                        category: String::new(),
                        expression: format!("{{{{ {path} }}}}"),
                    })
                    .collect();
                match template::export(&self.design, &fields) {
                    Ok(content) => {
                        let name = app.get_template_name().to_string();
                        if name.trim().is_empty() {
                            self.error("请填写模板名称。");
                            return;
                        }
                        let (op, id, version) = self
                            .template
                            .as_ref()
                            .map(|t| {
                                (
                                    SAVE_USER_REPORT_TEMPLATE_DRAFT,
                                    t.id,
                                    Some(t.version_number),
                                )
                            })
                            .unwrap_or((CREATE_USER_REPORT_TEMPLATE, 0, None));
                        self.request(op,id,vec![],Some(json!({"name":name,"reportType":"ExportDocument","contentHtml":content,"expectedVersion":version})),"template-saved");
                    }
                    Err(e) => self.error(e),
                }
                return;
            }
            "publish" => {
                if self.design_history.can_undo() {
                    self.error("请先保存模板修改。");
                    return;
                }
                if let Some(t) = &self.template {
                    self.request(
                        PUBLISH_USER_REPORT_TEMPLATE,
                        t.id,
                        vec![],
                        Some(json!({"expectedVersion":t.version_number})),
                        "template-saved",
                    );
                }
                return;
            }
            "undo" => {
                self.design_history.undo(&mut self.design);
                self.sync_design();
                self.sync_design_fields();
                return;
            }
            "redo" => {
                self.design_history.redo(&mut self.design);
                self.sync_design();
                self.sync_design_fields();
                return;
            }
            "delete" => self
                .design
                .remove_selection(&BTreeSet::from([self.selected_element.clone()])),
            "bind" => {
                if let Some((path, _)) = self.bindings.get(app.get_binding_index().max(0) as usize)
                {
                    if let Some(e) = self.design.element_mut(&self.selected_element) {
                        e.kind = Kind::Field {
                            field_path: path.clone(),
                            fallback_text: String::new(),
                        };
                    }
                }
            }
            "明细表" => {
                if let Some(e) = self
                    .design
                    .layers
                    .iter()
                    .flat_map(|l| &l.elements)
                    .find(|e| matches!(e.kind, Kind::Flow { .. }))
                {
                    self.selected_element = e.id.clone();
                }
            }
            _ => {
                let kind = match action {
                    "文字" => Kind::Text {
                        text: "输入文字".into(),
                    },
                    "字段" => Kind::Field {
                        field_path: "Invoice.InvoiceNo".into(),
                        fallback_text: String::new(),
                    },
                    "线条" => Kind::Line {
                        direction: "Horizontal".into(),
                    },
                    "矩形" => Kind::Rectangle,
                    _ => return,
                };
                match self.design.add(kind, 0) {
                    Ok(id) => self.selected_element = id,
                    Err(e) => self.error(e),
                }
            }
        }
        self.design_history.record(before, &self.design, None);
        self.sync_design();
        self.sync_design_fields();
    }
}
