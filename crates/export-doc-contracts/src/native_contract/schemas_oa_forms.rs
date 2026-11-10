//! Stable custom-form API; adding an application never creates routes or SQL tables.
use super::model::*;
fn scalar() -> Vec<(&'static str, Schema)> {
    vec![
        ("key", s().pattern("^[A-Za-z0-9_]{1,40}$")),
        ("label", s().min_length(1).max_length(100)),
        (
            "kind",
            s().enumeration(Enumeration::Values(
                [
                    "Text",
                    "Multiline",
                    "Decimal",
                    "Money",
                    "Date",
                    "Select",
                    "Person",
                    "Department",
                    "Table",
                ]
                .into_iter()
                .map(serde_json::Value::from)
                .collect(),
            )),
        ),
        ("required", b()),
        (
            "options",
            a(s().max_length(100)).max_items(50).unique_items(true),
        ),
        ("currency", s().max_length(3)),
    ]
}
pub(super) fn entries() -> Vec<(&'static str, Schema)> {
    let mut field = scalar();
    field.extend([
        ("columns", a(r("OaFormColumn")).max_items(10)),
        (
            "visibleWhen",
            Schema::default().any_of(vec![r("OaFormVisibility"), ty(Primitive::Null)]),
        ),
    ]);
    vec![
        (
            "OaFormReference",
            o(vec![("id", s()), ("name", s())]).required(vec!["id", "name"]),
        ),
        (
            "OaFormReferencePage",
            super::schema_shapes::office_page("OaFormReference"),
        ),
        (
            "OaFormVisibility",
            o(vec![
                ("field", s().max_length(40)),
                ("equals", s().max_length(100)),
            ])
            .required(vec!["field", "equals"]),
        ),
        (
            "OaFormColumn",
            o(scalar()).required(vec!["key", "label", "kind"]),
        ),
        (
            "OaFormField",
            o(field).required(vec!["key", "label", "kind"]),
        ),
        (
            "OaFormDefinition",
            o(vec![
                ("category", s().min_length(1).max_length(50)),
                (
                    "departmentIds",
                    a(s().max_length(50)).max_items(100).unique_items(true),
                ),
                ("attachmentRequired", b()),
                ("fields", a(r("OaFormField")).min_items(1).max_items(40)),
            ])
            .required(vec!["category", "fields"]),
        ),
        (
            "OaFormRow",
            ty(Primitive::Object)
                .additional(Additional::Schema(Box::new(s().max_length(2000))))
                .max_properties(10),
        ),
        (
            "OaFormValues",
            ty(Primitive::Object)
                .additional(Additional::Schema(Box::new(Schema::default().one_of(
                    vec![s().max_length(2000), a(r("OaFormRow")).max_items(100)],
                ))))
                .max_properties(40),
        ),
        (
            "OaApplication",
            o(vec![
                ("key", s()),
                ("name", s()),
                ("version", i().format("int64")),
                ("form", r("OaFormDefinition")),
            ])
            .required(vec!["key", "name", "version", "form"]),
        ),
        (
            "OaApplicationCatalog",
            super::schema_shapes::item_collection("OaApplication"),
        ),
        (
            "OaSettingsPatch",
            o(vec![
                (
                    "section",
                    s().enumeration(Enumeration::Values(
                        [
                            "rules",
                            "calendar",
                            "delegations",
                            "handlingServices",
                            "sheetTemplates",
                        ]
                        .into_iter()
                        .map(serde_json::Value::from)
                        .collect(),
                    )),
                ),
                ("expectedRevision", i().minimum(num("0"))),
                ("definitionKey", s()),
                ("remove", b()),
                ("rule", r("OaApprovalRule")),
                ("calendar", r("OaBusinessCalendar")),
                ("delegations", a(r("OaApprovalDelegation")).max_items(50)),
                (
                    "handlingServices",
                    a(r("OfficeHandlingService")).max_items(100),
                ),
                ("sheetTemplates", a(r("OaSheetTemplate")).max_items(18)),
            ])
            .required(vec!["section", "expectedRevision"]),
        ),
    ]
}
