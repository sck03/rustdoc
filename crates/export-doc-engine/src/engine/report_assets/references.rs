//! Targeted reference checks; empty pages never scan business documents.
use super::*;
use std::collections::{HashMap, HashSet};

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
        Self::query(tx, actor, paths, None)
    }

    pub fn query(
        tx: &Connection,
        actor: &Actor,
        paths: Option<&crate::paths::RuntimePaths>,
        targets: Option<&[&str]>,
    ) -> Result<Self> {
        let mut index = Self::default();
        if targets.is_some_and(<[&str]>::is_empty) {
            return Ok(index);
        }
        let mut visible_templates = HashMap::new();
        for (kind, permission) in [
            ("invoices", "document.invoices"),
            ("exporters", "document.master-data"),
            ("report-templates", "document.report-templates"),
            ("template-versions", ""),
        ] {
            let mut cursor = None;
            loop {
                let (next, rows) = tx.resource_references(kind, targets, cursor, 200)?;
                if rows.is_empty() {
                    break;
                }
                cursor = next;
                let last = rows.len() < 200;
                for record in rows {
                    crate::operation::check()?;
                    let visible = if kind == "report-templates" {
                        template_visible(actor, &record)
                    } else if kind == "template-versions" {
                        if let Some(id) = record["templateId"].as_i64() {
                            if let std::collections::hash_map::Entry::Vacant(entry) =
                                visible_templates.entry(id)
                            {
                                let visible = tx
                                    .report_template_metadata(id)?
                                    .is_some_and(|template| template_visible(actor, &template));
                                entry.insert(visible);
                            }
                            visible_templates[&id]
                        } else {
                            false
                        }
                    } else {
                        !permission.is_empty() && auth::visible(actor, permission, "view", &record)
                    };
                    if kind == "report-templates" {
                        if let Some(id) = record["id"].as_i64() {
                            visible_templates.insert(id, visible);
                        }
                    }
                    let mut ids = HashSet::new();
                    collect(kind, &record, &mut ids)?;
                    if let Some(targets) = targets {
                        ids.retain(|id| targets.contains(&id.as_str()));
                    }
                    if visible
                        || (kind == "report-templates"
                            && super::super::report_templates::policy::output_visible(
                                actor, &record,
                            ))
                    {
                        index.readable.extend(ids.iter().cloned());
                    }
                    index.all.extend(ids);
                }
                if last {
                    break;
                }
            }
        }
        if let Some(paths) = paths {
            for (id, visible) in report_template_files::resource_access(paths, actor)? {
                if targets.is_some_and(|targets| !targets.contains(&id.as_str())) {
                    continue;
                }
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
        "invoices" => {
            if value["shippingMarksType"] == "Image" {
                add_path("shippingMarksImage", "Files/ShippingMarks/")?;
            }
            add_path("docSealPath", "Files/Seals/")?;
            add_path("customsSealPath", "Files/Seals/")?;
        }
        "exporters" => {
            add_path("docSealPath", "Files/Seals/")?;
            add_path("customsSealPath", "Files/Seals/")?;
        }
        "report-templates" | "template-versions" => {
            if value["validDocument"] != true {
                return Err(unavailable("模板资源索引损坏，已停止图片操作。"));
            }
            if !value["resources"].is_null() && !value["resources"].is_array() {
                return Err(unavailable("模板资源索引损坏。"));
            }
            for resource in value["resources"].as_array().into_iter().flatten() {
                let id = text(resource, "id");
                resource_id(&id).map_err(|_| unavailable("模板资源编号损坏。"))?;
                ids.insert(id);
            }
        }
        _ => (),
    }
    Ok(())
}
