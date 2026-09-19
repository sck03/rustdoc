//! Personnel drafts and presentation are independent of the native event loop.
use crate::{
    FormSection,
    form_model::{FormModel, Lookups},
    form_sections::{self, DisclosureState, Section},
};
use export_doc_engine::{contracts, generated_api::*};
use serde_json::{Value, json};

#[derive(Default)]
pub struct PersonnelModel {
    pub departments: Vec<PersonnelDepartmentRecord>,
    pub directory: Vec<PersonnelDirectoryRecord>,
    pub record: Option<PersonnelRecord>,
    pub clearance: Vec<PersonnelClearanceItem>,
    pub tab: i32,
    pub image_kind: usize,
    pub show_identity: bool,
    pub history_page: i64,
}
pub const IMAGE_KINDS: [&str; 3] = ["Avatar", "IdentityFront", "IdentityBack"];

pub fn form(
    record: Option<&PersonnelRecord>,
    date: &str,
    departments: &[PersonnelDepartmentRecord],
    mut lookups: Lookups,
) -> Result<FormModel, String> {
    let operation = if record.is_some() {
        UPDATE_PERSONNEL
    } else {
        CREATE_PERSONNEL
    };
    let mut schema = contracts::resolve(contracts::request(operation.id)).clone();
    let mut value = contracts::object(operation.id, true);
    if let Some(record) = record {
        value = contracts::overlay(value, &json!(record));
        value["expectedVersion"] = json!(record.version_number);
        value["registration"] = if record.can_correct_registration {
            json!({"employeeNumber":record.employee.employee_number,"departmentId":record.employee.department_id,"jobTitle":record.employee.job_title,"hireDate":record.hire_date,"onProbation":record.employee.status=="Probation"})
        } else {
            schema["properties"]["registration"]["readOnly"] = json!(true);
            Value::Null
        };
    } else {
        value["requestKey"] = json!(export_doc_engine::paths::nonce()?);
        value["employmentType"] = json!("FullTime");
        value["hireDate"] = json!(date);
        value["onProbation"] = json!(true);
        if let Some(department) = departments.iter().find(|department| department.is_active) {
            value["departmentId"] = json!(department.code);
        }
    }
    lookups.insert(
        "departmentId".into(),
        departments
            .iter()
            .filter(|department| {
                department.is_active
                    || record.is_some_and(|record| record.employee.department_id == department.code)
            })
            .map(|department| (json!(department.code), department.name.clone()))
            .collect(),
    );
    Ok(FormModel::new(&schema, value, lookups))
}

pub fn sections(form: &FormModel, state: &mut DisclosureState) -> Vec<FormSection> {
    let definitions = [
        Section::new(
            "person.basic",
            "基本资料",
            &[
                "employeeNumber",
                "employee.employeeNumber",
                "registration.employeeNumber",
                "profile.fullName",
                "departmentId",
                "employee.departmentName",
                "registration.departmentId",
                "jobTitle",
                "employee.jobTitle",
                "registration.jobTitle",
                "hireDate",
                "registration.hireDate",
                "onProbation",
                "registration.onProbation",
                "employee.status",
                "employmentType",
                "probationEndsOn",
                "contractEndsOn",
            ],
            true,
        ),
        Section::new(
            "person.work",
            "工作联系方式 · 公司通讯录可见",
            &[
                "profile.workEmail",
                "profile.workPhone",
                "profile.workLocation",
            ],
            true,
        ),
        Section::new(
            "person.private",
            "个人联系与人事备注",
            &[
                "profile.personalPhone",
                "profile.emergencyContact",
                "profile.emergencyPhone",
                "profile.notes",
            ],
            false,
        ),
        Section::new(
            "person.identity",
            "身份证件资料",
            &[
                "profile.identityNumber",
                "profile.identityAuthority",
                "profile.registeredAddress",
                "profile.identityValidFrom",
                "profile.identityValidUntil",
                "profile.identityLongTerm",
            ],
            false,
        ),
    ];
    form_sections::build(form, &definitions, state)
}

pub fn facts(
    record: &PersonnelRecord,
    show_identity: bool,
    state: &mut DisclosureState,
) -> Vec<FormSection> {
    let mut value = json!(record);
    if !show_identity {
        let number = &record.profile.identity_number;
        if number.chars().count() > 10 {
            let chars: Vec<_> = number.chars().collect();
            value["profile"]["identityNumber"] = json!(format!(
                "{}********{}",
                chars[..6].iter().collect::<String>(),
                chars[chars.len() - 4..].iter().collect::<String>()
            ));
        }
    }
    let form = FormModel::new(contracts::schema("PersonnelRecord"), value, Lookups::new());
    let mut sections = sections(&form, state);
    sections.extend(form_sections::build(
        &form,
        &[Section::new(
            "person.employment",
            "任职日期",
            &["lastEffectiveDate", "confirmedOn", "departedOn"],
            false,
        )],
        state,
    ));
    sections
}

pub fn reminder(record: &PersonnelRecord, business_date: &str) -> String {
    let Ok(today) = chrono::NaiveDate::parse_from_str(business_date, "%Y-%m-%d") else {
        return String::new();
    };
    let Some(soon) = today.checked_add_signed(chrono::Duration::days(30)) else {
        return String::new();
    };
    if record.employee.status == "Departed" {
        return String::new();
    }
    [
        (
            "试用期",
            record
                .probation_ends_on
                .as_deref()
                .filter(|_| record.employee.status == "Probation"),
        ),
        ("合同", record.contract_ends_on.as_deref()),
        (
            "身份证",
            record
                .profile
                .identity_valid_until
                .as_deref()
                .filter(|_| !record.profile.identity_long_term),
        ),
    ]
    .into_iter()
    .filter_map(|(label, day)| {
        day.and_then(|day| {
            chrono::NaiveDate::parse_from_str(day, "%Y-%m-%d")
                .ok()
                .filter(|date| *date <= soon)
                .map(|date| {
                    format!(
                        "{label}{}：{date}",
                        if date < today {
                            "已到期"
                        } else {
                            "即将到期"
                        }
                    )
                })
        })
    })
    .collect::<Vec<_>>()
    .join("；")
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn editor_preserves_private_fields_and_requires_explicit_registration_permission() {
        let mut person = PersonnelRecord::default();
        person.employee.id = 3;
        person.version_number = 7;
        person.employee.employee_number = "E3".into();
        person.profile.personal_phone = "123".into();
        let draft = form(Some(&person), "2026-09-17", &[], Lookups::new()).unwrap();
        assert_eq!(draft.value["profile"]["personalPhone"], "123");
        assert_eq!(draft.value["expectedVersion"], 7);
        assert!(draft.value["registration"].is_null());
        person.can_correct_registration = true;
        let draft = form(Some(&person), "2026-09-17", &[], Lookups::new()).unwrap();
        assert_eq!(draft.value["registration"]["employeeNumber"], "E3");
    }
}
