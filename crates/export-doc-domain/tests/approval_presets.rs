use export_doc_domain::permissions::{Role, resource, role_grants};

#[test]
fn approval_presets_have_bounded_review_rights_without_management() {
    let presets: Vec<Role> = serde_json::from_str(include_str!(
        "../../export-doc-contracts/tests/fixtures/approval-presets.json"
    ))
    .unwrap();
    for preset in presets {
        let grants = role_grants(&preset.code).unwrap();
        assert!(
            preset
                .grants
                .iter()
                .filter(|grant| resource(&grant.resource_key).unwrap().supports_data_scope)
                .all(|grant| grants.contains(grant)),
            "{}",
            preset.code
        );
        assert!(!grants.iter().any(|g| {
            !["view", "approve", "create", "edit", "cancel"].contains(&g.action.as_str())
        }));
        assert!(!grants.iter().any(|g| g.data_scope == "all"
            && resource(&g.resource_key).is_some_and(|r| r.supports_data_scope)));
    }
    assert!(
        !role_grants("OfficeEmployee")
            .unwrap()
            .iter()
            .any(|g| g.action == "approve")
    );
}
