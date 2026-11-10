//! Resolve each distinct reference once in the submission's authorized snapshot.
use super::*;
use export_doc_domain::oa_form::{FieldKind, FormField, nonblank};
use std::collections::{BTreeMap, BTreeSet};

fn visit(
    fields: &[FormField],
    values: &FormValues,
    action: &mut impl FnMut(&FieldKind, &str) -> Result<Value>,
) -> Result<FormValues> {
    let mut display = values.clone();
    for field in fields {
        let Some(value) = values.get(&field.key) else {
            continue;
        };
        if field.kind == FieldKind::Table {
            let rows = value
                .as_array()
                .into_iter()
                .flatten()
                .map(|row| {
                    let values = serde_json::from_value(row.clone())
                        .map_err(|_| invalid("明细格式无效。"))?;
                    visit(&field.columns, &values, action)
                })
                .collect::<Result<Vec<_>>>()?;
            display.insert(field.key.clone(), json!(rows));
        } else if matches!(field.kind, FieldKind::Person | FieldKind::Department) {
            if let Some(value) = nonblank(value) {
                display.insert(field.key.clone(), action(&field.kind, value)?);
            }
        }
    }
    Ok(display)
}

pub(super) fn display_values(
    tx: &Connection,
    actor: &Actor,
    fields: &[FormField],
    values: &FormValues,
) -> Result<FormValues> {
    let mut people = BTreeSet::new();
    let mut departments = BTreeSet::new();
    visit(fields, values, &mut |kind, value| {
        match kind {
            FieldKind::Person => {
                people.insert(
                    value
                        .parse::<i64>()
                        .map_err(|_| invalid("人员编号无效。"))?,
                );
            }
            FieldKind::Department => {
                departments.insert(store::department_identity(&actor.company, value)?);
            }
            _ => unreachable!(),
        }
        Ok(Value::Null)
    })?;
    let mut names = BTreeMap::new();
    for person in tx.records_by_ids("people", &people.into_iter().collect::<Vec<_>>(), &[])? {
        super::super::super::personnel_organization::validate_active_person(
            tx,
            &actor.company,
            &person,
        )?;
        let id = records::positive(&person, "id", "人员")?;
        names.insert(
            id,
            format!("{}（{id}）", text(&person["profile"], "fullName")),
        );
    }
    let mut department_names = BTreeMap::new();
    for department in tx.records_by_identities(
        "departments",
        &departments.into_iter().collect::<Vec<_>>(),
        &[],
    )? {
        if department["isActive"] != true {
            return Err(invalid("请选择本单位的启用部门。"));
        }
        department_names.insert(
            store::department_identity(&actor.company, &text(&department, "code"))?,
            text(&department, "name"),
        );
    }
    visit(fields, values, &mut |kind, value| {
        Ok(json!(match kind {
            FieldKind::Person => names
                .get(
                    &value
                        .parse::<i64>()
                        .map_err(|_| invalid("人员编号无效。"))?
                )
                .cloned()
                .ok_or_else(|| error(404, "人员记录不存在。"))?,
            FieldKind::Department => format!(
                "{}（{value}）",
                department_names
                    .get(&store::department_identity(&actor.company, value)?)
                    .ok_or_else(|| invalid("请选择本单位的启用部门。"))?
            ),
            _ => unreachable!(),
        }))
    })
}
