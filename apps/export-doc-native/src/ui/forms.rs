use super::{labels, theme};
use eframe::egui::{self, RichText};
use export_doc_native::{contracts, workspace::display};
use serde_json::{Value, json};
use std::collections::BTreeMap;

pub fn form(
    ui: &mut egui::Ui,
    schema: &Value,
    value: &mut Value,
    buffers: &mut BTreeMap<String, String>,
    lookups: &BTreeMap<String, Vec<(Value, String)>>,
    prefix: &str,
    probes: &mut BTreeMap<String, egui::Rect>,
) -> Option<String> {
    let Some(properties) = contracts::properties(schema) else {
        return None;
    };
    let fields: Vec<_> = properties
        .iter()
        .filter(|(key, _)| !labels::internal(key))
        .collect();
    let mut invalid = None;
    let simple: Vec<_> = fields
        .iter()
        .copied()
        .filter(|(_, schema)| !["object", "array"].contains(&contracts::kind(schema)))
        .collect();
    let columns = if ui.available_width() > 850. {
        3
    } else if ui.available_width() > 460. {
        2
    } else {
        1
    };
    for row in simple.chunks(columns) {
        ui.columns(columns, |columns| {
            for (index, (key, schema)) in row.iter().enumerate() {
                let path = format!("{prefix}.{key}");
                let ui = &mut columns[index];
                let label = labels::label(key);
                ui.label(RichText::new(&label).size(13.).color(theme::MUTED));
                let current = &mut value[*key];
                let response = if let Some(options) = lookups.get(key.as_str()) {
                    egui::ComboBox::from_id_salt(&path)
                        .width(ui.available_width())
                        .selected_text(
                            options
                                .iter()
                                .find(|(option, _)| option == current)
                                .map(|(_, label)| label.clone())
                                .unwrap_or_else(|| "请选择".into()),
                        )
                        .show_ui(ui, |ui| {
                            if contracts::nullable(schema) {
                                ui.selectable_value(current, Value::Null, "未选择");
                            }
                            for (option, label) in options {
                                ui.selectable_value(current, option.clone(), label);
                            }
                        })
                        .response
                } else if let Some(options) = choices(key, schema) {
                    egui::ComboBox::from_id_salt(&path)
                        .width(ui.available_width())
                        .selected_text(display(current))
                        .show_ui(ui, |ui| {
                            for (option, label) in options {
                                ui.selectable_value(current, option, label);
                            }
                        })
                        .response
                } else if contracts::kind(schema) == "boolean" {
                    let mut checked = current.as_bool().unwrap_or(false);
                    let response = ui.checkbox(&mut checked, "是");
                    if response.changed() {
                        *current = json!(checked);
                    }
                    response
                } else {
                    let field = buffers
                        .entry(path.clone())
                        .or_insert_with(|| match current {
                            Value::String(text) => text.clone(),
                            Value::Null => String::new(),
                            _ => current.to_string(),
                        });
                    let multiline = [
                        "notes",
                        "note",
                        "summary",
                        "nextAction",
                        "addressEN",
                        "addressCN",
                        "description",
                        "bodyHtml",
                        "contentHtml",
                        "mainProducts",
                        "elements",
                    ]
                    .contains(&key.as_str());
                    let hint = match schema["format"].as_str() {
                        Some("date") => "YYYY-MM-DD",
                        Some("date-time") => "YYYY-MM-DDTHH:MM+08:00",
                        _ => "",
                    };
                    let edit = if multiline {
                        egui::TextEdit::multiline(field).desired_rows(3)
                    } else {
                        egui::TextEdit::singleline(field)
                            .password(key.to_lowercase().contains("password"))
                    };
                    let response = ui.add_sized(
                        [ui.available_width(), if multiline { 76. } else { 34. }],
                        edit.id_salt(&path).hint_text(hint),
                    );
                    match contracts::kind(schema) {
                        "integer" => {
                            if field.trim().is_empty() && contracts::nullable(schema) {
                                *current = Value::Null;
                            } else {
                                match field.trim().parse::<i64>() {
                                    Ok(number) => *current = json!(number),
                                    Err(_) => {
                                        invalid = Some(format!("{label}请输入整数。"));
                                        ui.painter().rect_stroke(
                                            response.rect,
                                            6.,
                                            egui::Stroke::new(1., theme::ERROR),
                                            egui::StrokeKind::Inside,
                                        );
                                    }
                                }
                            }
                        }
                        "number" => {
                            if field.trim().is_empty() && contracts::nullable(schema) {
                                *current = Value::Null;
                            } else {
                                match export_doc_native::invoice::parse_number(field) {
                                    Ok(number) => *current = json!(number),
                                    Err(_) => {
                                        invalid = Some(format!("{label}请输入有效数字。"));
                                        ui.painter().rect_stroke(
                                            response.rect,
                                            6.,
                                            egui::Stroke::new(1., theme::ERROR),
                                            egui::StrokeKind::Inside,
                                        );
                                    }
                                }
                            }
                        }
                        _ => {
                            *current = if field.is_empty() && contracts::nullable(schema) {
                                Value::Null
                            } else {
                                json!(field)
                            }
                        }
                    }
                    response
                };
                probes.insert(format!("field-{key}"), response.rect);
            }
        });
    }
    for (key, schema) in fields
        .into_iter()
        .filter(|(_, schema)| ["object", "array"].contains(&contracts::kind(schema)))
    {
        ui.add_space(8.);
        let path = format!("{prefix}.{key}");
        if contracts::kind(schema) == "object" {
            egui::CollapsingHeader::new(labels::label(key))
                .id_salt(&path)
                .default_open(key == "profile")
                .show(ui, |ui| {
                    if value[key].is_null() {
                        if ui.button("填写资料").clicked() {
                            value[key] = contracts::initial(contracts::resolve(schema));
                        }
                        return;
                    }
                    if let Some(error) =
                        form(ui, schema, &mut value[key], buffers, lookups, &path, probes)
                    {
                        invalid = Some(error);
                    }
                });
        } else {
            egui::CollapsingHeader::new(format!(
                "{} · {} 项",
                labels::label(key),
                value[key].as_array().map(Vec::len).unwrap_or(0)
            ))
            .id_salt(&path)
            .show(ui, |ui| {
                if value[key].is_null() {
                    value[key] = json!([]);
                }
                let Some(values) = value[key].as_array_mut() else {
                    return;
                };
                let mut remove = None;
                for (index, item) in values.iter_mut().enumerate() {
                    ui.push_id(index, |ui| {
                        theme::card().inner_margin(10).show(ui, |ui| {
                            ui.horizontal(|ui| {
                                ui.strong(format!("第 {} 项", index + 1));
                                if ui.small_button("移除").clicked() {
                                    remove = Some(index);
                                }
                            });
                            if contracts::kind(&schema["items"]) == "object" {
                                if let Some(error) = form(
                                    ui,
                                    &schema["items"],
                                    item,
                                    buffers,
                                    lookups,
                                    &format!("{path}.{index}"),
                                    probes,
                                ) {
                                    invalid = Some(error);
                                }
                            } else {
                                let text = buffers
                                    .entry(format!("{path}.{index}"))
                                    .or_insert_with(|| display(item));
                                if ui.text_edit_singleline(text).changed() {
                                    *item = json!(text);
                                }
                            }
                        });
                    });
                }
                if let Some(index) = remove {
                    values.remove(index);
                    buffers.retain(|key, _| !key.starts_with(&path));
                }
                if values.len() < 5000 && ui.button("＋ 添加一项").clicked() {
                    values.push(contracts::initial(&schema["items"]));
                }
            });
        }
    }
    invalid
}
fn choices(key: &str, schema: &Value) -> Option<Vec<(Value, String)>> {
    let options: &[(&str, &str)] = match key {
        "employmentType" => &[
            ("FullTime", "全职"),
            ("PartTime", "兼职"),
            ("Intern", "实习"),
            ("Contractor", "合同工"),
        ],
        "role" => &[
            ("User", "普通用户"),
            ("Admin", "管理员"),
            ("Sales", "业务员"),
            ("Document", "单证员"),
            ("Administration", "行政"),
        ],
        "nextStage" => &[
            ("线索", "线索"),
            ("需求确认", "需求确认"),
            ("已报价", "已报价"),
            ("谈判中", "谈判中"),
            ("已成交", "已成交"),
            ("已失单", "已失单"),
        ],
        "targetStatus" => &[
            ("Verified", "已核对"),
            ("Shipped", "已出运"),
            ("Completed", "已结汇"),
            ("Cancelled", "已作废"),
        ],
        "dataScope" => &[
            ("Own", "本人"),
            ("Department", "本部门"),
            ("Company", "本公司"),
            ("All", "全部"),
        ],
        "currency" => &[
            ("USD", "USD 美元"),
            ("CNY", "CNY 人民币"),
            ("EUR", "EUR 欧元"),
            ("GBP", "GBP 英镑"),
            ("JPY", "JPY 日元"),
            ("HKD", "HKD 港币"),
        ],
        "priceCalculationMode" => &[
            ("UnitPriceDriven", "按单价计算金额"),
            ("LineAmountDriven", "按金额反算单价"),
        ],
        "notifyPartyMode" => &[
            ("None", "无通知人"),
            ("SameAsConsignee", "同收货人"),
            ("Separate", "单独填写"),
        ],
        _ => &[],
    };
    if !options.is_empty() {
        return Some(
            options
                .iter()
                .map(|(value, label)| (json!(value), (*label).into()))
                .collect(),
        );
    }
    contracts::resolve(schema)["enum"]
        .as_array()
        .map(|options| {
            options
                .iter()
                .map(|value| (value.clone(), display(value)))
                .collect()
        })
}
