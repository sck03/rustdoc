//! One reference scan per operation, independent of the image catalog size.
use super::*;
use std::collections::HashSet;

#[derive(Default)]
pub(super) struct References {
    pub all: HashSet<String>,
    pub readable: HashSet<String>,
}

impl References {
    pub fn load(
        tx: &Connection,
        actor: &Actor,
        paths: Option<&crate::paths::RuntimePaths>,
    ) -> Result<Self> {
        let mut index = Self::default();
        let mut visible_templates = HashSet::new();
        for (kind, permission) in [
            ("invoices", "document.invoices"),
            ("exporters", "document.master-data"),
            ("report-templates", "document.report-templates"),
            ("template-versions", ""),
        ] {
            for record in tx.all(kind)? {
                crate::operation::check()?;
                if kind == "template-versions" && record["templateKind"] != "report-templates" {
                    continue;
                }
                let visible = if kind == "report-templates" {
                    template_visible(actor, &record)
                } else if kind == "template-versions" {
                    record["templateId"]
                        .as_i64()
                        .is_some_and(|id| visible_templates.contains(&id))
                } else {
                    !permission.is_empty() && auth::visible(actor, permission, "view", &record)
                };
                if kind == "report-templates" && visible {
                    if let Some(id) = record["id"].as_i64() {
                        visible_templates.insert(id);
                    }
                }
                let mut ids = HashSet::new();
                collect(kind, &record, &mut ids)?;
                if visible {
                    index.readable.extend(ids.iter().cloned());
                }
                index.all.extend(ids);
            }
        }
        if let Some(paths) = paths {
            for (id, visible) in report_template_files::resource_access(paths, actor)? {
                if visible {
                    index.readable.insert(id.clone());
                }
                index.all.insert(id);
            }
        }
        Ok(index)
    }
}

fn collect(kind: &str, value: &Value, ids: &mut HashSet<String>) -> Result<()> {
    let mut add_path = |field: &str, prefix: &str| -> Result<()> {
        let path = text(value, field);
        if !path.is_empty() {
            ids.insert(
                stored_id(&path, prefix)
                    .map_err(|_| unavailable("已保存的图片引用损坏。"))?
                    .to_owned(),
            );
        }
        Ok(())
    };
    match kind {
        "invoices" if value["shippingMarksType"] == "Image" => {
            add_path("shippingMarksImage", "Files/ShippingMarks/")?
        }
        "exporters" => {
            add_path("docSealPath", "Files/Seals/")?;
            add_path("customsSealPath", "Files/Seals/")?;
        }
        "report-templates" | "template-versions" => {
            let template = if kind == "template-versions" {
                &value["content"]
            } else {
                value
            };
            let source = template["contentHtml"]
                .as_str()
                .ok_or_else(|| unavailable("模板资源索引损坏。"))?;
            let root =
                schema(source).map_err(|_| unavailable("模板资源索引损坏，已停止图片操作。"))?;
            for resource in root["resources"].as_array().into_iter().flatten() {
                let id = text(resource, "id");
                resource_id(&id).map_err(|_| unavailable("模板资源编号损坏。"))?;
                ids.insert(id);
            }
        }
        _ => (),
    }
    Ok(())
}
