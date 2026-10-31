//! Independent evaluation of the original annotations protects compiled access semantics.
use export_doc_domain::{
    contracts,
    generated_api::*,
    permissions::{self, Grant, ProductEdition},
};
use serde_json::Value;

fn rank(value: &str) -> u8 {
    match value {
        "view" => 1,
        "operate" => 2,
        "manage" => 3,
        _ => 0,
    }
}
fn prior_action<'a>(key: &str, action: &'a str) -> &'a str {
    // The approved directory repair preserves view and maps only mutations to manage.
    if key == "organization.directory" && matches!(action, "create" | "edit" | "delete") {
        return "manage";
    }
    let Some(resource) = permissions::resource(key) else {
        return action;
    };
    if resource.actions.iter().any(|a| a.key == action) {
        return action;
    }
    if resource.actions.iter().any(|a| a.key == "operate")
        && resource.actions.iter().any(|a| a.key == "manage")
    {
        match action {
            "create" | "edit" => "operate",
            "delete" => "manage",
            _ => action,
        }
    } else {
        action
    }
}
fn prior_decision(
    grants: &[Grant],
    admin: bool,
    edition: ProductEdition,
    op: Operation,
    query: &[(&str, String)],
) -> bool {
    let policy = &contracts::contract()["operations"][op.id]["policy"];
    if !policy.is_object() {
        return false;
    }
    let allows = |key: &str, action: &str| {
        edition.allows(key)
            && !action.is_empty()
            && (admin
                || !key.starts_with("system.")
                    && grants
                        .iter()
                        .any(|g| g.resource_key == key && g.action == prior_action(key, action)))
    };
    if let Some(requirements) = policy["requirements"]
        .as_array()
        .filter(|items| !items.is_empty())
    {
        return requirements.iter().all(|r| {
            allows(
                r["resourceKey"].as_str().unwrap_or(""),
                r["action"].as_str().unwrap_or(""),
            )
        });
    }
    if policy["permissionBypass"] == true {
        return true;
    }
    let metadata = &policy["permissions"];
    if !metadata.is_object() {
        return false;
    }
    let read = op.method == "GET";
    let mut module = if read {
        metadata["readModule"].as_str()
    } else {
        metadata["writeModule"]
            .as_str()
            .or(metadata["readModule"].as_str())
    }
    .unwrap_or("");
    if read && metadata["selector"] == 1 {
        module = match query
            .iter()
            .find(|(key, _)| *key == "reportType")
            .map(|(_, value)| value.as_str())
        {
            Some("PaymentVoucher") => "document.payment-reports",
            Some("ExportDocument") => "document.invoice-reports",
            _ => module,
        };
    }
    if module.is_empty() {
        return false;
    }
    let level = if read {
        metadata["readAccessLevel"].as_str().unwrap_or("view")
    } else {
        metadata["writeAccessLevel"]
            .as_str()
            .unwrap_or(if op.method == "DELETE" {
                "manage"
            } else {
                "operate"
            })
    };
    if permissions::resource(module).is_some() {
        return allows(module, level);
    }
    let catalog = permissions::catalog();
    rank(level) > 0
        && catalog
            .resources
            .iter()
            .any(|r| r.module_key == module && edition.allows(&r.key))
        && (admin
            || grants.iter().any(|g| {
                edition.allows(&g.resource_key)
                    && catalog.resources.iter().any(|r| {
                        r.key == g.resource_key
                            && r.module_key == module
                            && r.actions.iter().any(|a| {
                                a.key == g.action && rank(&a.navigation_access_level) >= rank(level)
                            })
                    })
            }))
}

#[test]
fn compiled_access_preserves_annotations_across_editions_actions_roles_and_selectors() {
    let mut configurations = vec![
        ("empty".into(), vec![], false),
        ("administrator".into(), vec![], true),
    ];
    for resource in &permissions::catalog().resources {
        for action in &resource.actions {
            configurations.push((
                format!("{}:{}", resource.key, action.key),
                vec![Grant {
                    resource_key: resource.key.clone(),
                    action: action.key.clone(),
                    data_scope: "own".into(),
                }],
                false,
            ));
        }
    }
    for role in &permissions::catalog().roles {
        configurations.push((
            role.code.clone(),
            permissions::role_grants(&role.code).unwrap(),
            false,
        ));
    }
    for op in ALL_OPERATIONS {
        let policy: &Value = &contracts::contract()["operations"][op.id]["policy"];
        let selectors = if op.method == "GET" && policy["permissions"]["selector"] == 1 {
            vec![
                vec![],
                vec![("reportType", "PaymentVoucher".into())],
                vec![("reportType", "ExportDocument".into())],
                vec![("reportType", "unknown".into())],
                vec![("reportType", "paymentvoucher".into())],
            ]
        } else {
            vec![vec![]]
        };
        for edition in [
            ProductEdition::Full,
            ProductEdition::Document,
            ProductEdition::Sales,
        ] {
            for (name, grants, admin) in &configurations {
                for query in &selectors {
                    assert_eq!(
                        permissions::allows_edition_operation(grants, *admin, edition, *op, query),
                        prior_decision(grants, *admin, edition, *op, query),
                        "{} / {edition:?} / {name} / {query:?}",
                        op.id
                    );
                }
            }
        }
    }
}

#[test]
fn public_directory_view_does_not_allow_organization_mutations() {
    let grants = vec![Grant {
        resource_key: "organization.directory".into(),
        action: "view".into(),
        data_scope: "company".into(),
    }];
    assert!(permissions::allows_operation(
        &grants,
        false,
        LIST_ORGANIZATION_MANAGERS,
        &[]
    ));
    assert!(!permissions::allows_operation(
        &grants,
        false,
        CREATE_ORGANIZATION_COMPANY,
        &[]
    ));
    assert!(!permissions::allows_operation(
        &grants,
        false,
        DELETE_ORGANIZATION_DEPARTMENT,
        &[]
    ));
}

#[test]
fn employee_scheme_is_unit_assignable_without_global_record_access() {
    let employee = &permissions::catalog()
        .roles
        .iter()
        .find(|r| r.code == "OfficeEmployee")
        .unwrap()
        .grants;
    let grants = export_doc_domain::units::validate_grants(employee).unwrap();
    for key in ["office.announcements", "office.notifications"] {
        assert!(
            grants
                .iter()
                .any(|g| g.resource_key == key && g.action == "view" && g.data_scope == "all")
        );
        let resource = permissions::resource(key).unwrap();
        assert!(
            resource.unit_assignable
                && !resource.supports_data_scope
                && !resource.workflow_resource
        );
    }
    assert!(
        !permissions::resource("sales.supplier-assessments")
            .unwrap()
            .workflow_resource
    );
    assert!(
        permissions::resource("office.rooms")
            .unwrap()
            .workflow_resource
    );
    for (key, scope) in [("office.leave", "all"), ("system.users", "company")] {
        assert!(
            export_doc_domain::units::validate_grants(&[Grant {
                resource_key: key.into(),
                action: "view".into(),
                data_scope: scope.into()
            }])
            .is_err()
        );
    }
}
