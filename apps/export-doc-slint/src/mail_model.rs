use crate::{DataRow, model};
use export_doc_engine::history::History;
use serde_json::{Value, json};
use std::{collections::BTreeMap, path::PathBuf};

#[derive(Clone, Default, PartialEq)]
pub struct Compose {
    pub to: String,
    pub subject: String,
    pub text: String,
    pub html: Option<String>,
    pub attachments: Vec<PathBuf>,
}
impl Compose {
    pub fn body(&self) -> String {
        self.html
            .clone()
            .unwrap_or_else(|| export_doc_mail::content::plain_to_html(&self.text))
    }
    pub fn empty(&self) -> bool {
        self.to.is_empty()
            && self.subject.is_empty()
            && self.text.is_empty()
            && self.html.as_ref().is_none_or(String::is_empty)
            && self.attachments.is_empty()
    }
    pub fn request(&self) -> Value {
        json!({"toAddress":self.to.trim(),"subject":self.subject.trim(),"body":self.body(),"attachmentPaths":self.attachments})
    }
}
#[derive(Clone, Default, PartialEq)]
pub struct TemplateDraft {
    pub name: String,
    pub category: String,
    pub subject: String,
    pub html: String,
}
impl TemplateDraft {
    pub fn from(row: &Value) -> Self {
        Self {
            name: text(row, "name"),
            category: text(row, "category"),
            subject: text(row, "subject"),
            html: text(row, "bodyHtml"),
        }
    }
}
pub struct MailModel {
    pub compose: Compose,
    pub sent: Compose,
    pub pending: Option<Compose>,
    pub delivery_key: Option<(Value, String)>,
    pub templates: Vec<Value>,
    pub current: Option<Value>,
    pub template: TemplateDraft,
    pub baseline: TemplateDraft,
    pub template_history: History<TemplateDraft>,
    pub deliveries: Vec<Value>,
    pub versions: Vec<Value>,
    pub variables: Vec<Value>,
    pub values: BTreeMap<String, String>,
    pub preview: Option<Value>,
    pub customers: Vec<Value>,
    pub selected_delivery: usize,
    pub selected_version: usize,
    pub rich_source: bool,
}
impl Default for MailModel {
    fn default() -> Self {
        Self {
            compose: Default::default(),
            sent: Default::default(),
            pending: None,
            delivery_key: None,
            templates: vec![],
            current: None,
            template: Default::default(),
            baseline: Default::default(),
            template_history: History::new(60),
            deliveries: vec![],
            versions: vec![],
            variables: vec![],
            values: BTreeMap::new(),
            preview: None,
            customers: vec![],
            selected_delivery: 0,
            selected_version: 0,
            rich_source: false,
        }
    }
}
impl MailModel {
    pub fn dirty(&self) -> bool {
        (!self.compose.empty() && self.compose != self.sent) || self.template != self.baseline
    }
    pub fn open(&mut self, row: Option<Value>) {
        self.template = row
            .as_ref()
            .map(TemplateDraft::from)
            .unwrap_or_else(|| TemplateDraft {
                category: "通用".into(),
                ..Default::default()
            });
        self.baseline = self.template.clone();
        self.current = row;
        self.preview = None;
        self.versions.clear();
        self.template_history.clear();
    }
    pub fn template_body(&self) -> Value {
        json!({"name":self.template.name,"category":self.template.category,"subject":self.template.subject,"bodyHtml":self.template.html,"expectedVersion":self.current.as_ref().map(|r|r["versionNumber"].clone()).unwrap_or(json!(0))})
    }
    pub fn id(&self) -> i64 {
        self.current
            .as_ref()
            .and_then(|r| r["id"].as_i64())
            .unwrap_or(0)
    }
    pub fn template_rows(&self, scope: i32) -> Vec<DataRow> {
        self.templates
            .iter()
            .enumerate()
            .filter(|(_, row)| match scope {
                1 => row["canEdit"] == true,
                2 => row["shareScope"] != "Private",
                _ => true,
            })
            .map(|(i, r)| {
                row(
                    i,
                    [
                        text(r, "name"),
                        text(r, "category"),
                        text(r, "subject"),
                        label(&text(r, "status")),
                        label(&text(r, "shareScope")),
                    ],
                )
            })
            .collect()
    }
    pub fn delivery_rows(&self) -> Vec<DataRow> {
        self.deliveries
            .iter()
            .enumerate()
            .map(|(i, r)| {
                row(
                    i,
                    [
                        text(r, "recipient"),
                        text(r, "subject"),
                        label(&text(r, "status")),
                        text(r, "attachmentCount"),
                        text(r, "createdAt"),
                    ],
                )
            })
            .collect()
    }
    pub fn version_rows(&self) -> Vec<DataRow> {
        self.versions
            .iter()
            .enumerate()
            .map(|(i, r)| {
                row(
                    i,
                    [
                        text(r, "versionNumber"),
                        text(r, "changeType"),
                        text(r, "name"),
                        text(r, "changedBy"),
                        text(r, "createdAt"),
                    ],
                )
            })
            .collect()
    }
}
fn row(index: usize, fields: [String; 5]) -> DataRow {
    DataRow {
        id: index as i32 + 1,
        cells: model(fields.into_iter().map(Into::into).collect()),
    }
}
pub fn text(value: &Value, key: &str) -> String {
    value[key].as_str().map(str::to_owned).unwrap_or_else(|| {
        if value[key].is_number() {
            value[key].to_string()
        } else {
            String::new()
        }
    })
}
pub fn label(value: &str) -> String {
    match value {
        "Sent" => "已发送",
        "Attempting" => "尝试投递",
        "Uncertain" => "结果不确定",
        "Draft" => "草稿",
        "Published" => "已发布",
        "Disabled" => "已停用",
        "Archived" => "已归档",
        "Private" => "私有",
        "Department" => "部门共享",
        "Company" => "公司共享",
        "All" => "全部共享",
        _ => value,
    }
    .into()
}
