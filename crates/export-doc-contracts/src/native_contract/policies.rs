//! Authorization is declared once; endpoint factories preserve explicit access rules.
use super::model::*;

pub(super) fn action(key: &'static str, action: &'static str) -> Policy {
    rule(
        (true, true, true),
        None,
        &[(key, action)],
        access("All", &[(key, &[action], false)]),
        false,
    )
}
pub(super) fn access(
    mode: &'static str,
    resources: &[(&'static str, &[&'static str], bool)],
) -> Access {
    Access::Rule {
        r#match: mode,
        resources: resources
            .iter()
            .map(|(key, actions, admin)| AccessResource {
                resource_key: key,
                actions: actions.to_vec(),
                administrator_only: *admin,
            })
            .collect(),
    }
}
pub(super) fn rule(
    flags: (bool, bool, bool),
    permissions: Option<LegacyPermissions>,
    requirements: &[(&'static str, &'static str)],
    access: Access,
    permission_bypass: bool,
) -> Policy {
    Policy {
        requires_authentication: flags.0,
        requires_desktop_access: flags.1,
        requires_license: flags.2,
        permissions,
        permission_bypass,
        access,
        requirements: requirements
            .iter()
            .map(|(key, action)| Requirement {
                resource_key: key,
                action,
            })
            .collect(),
    }
}
pub(super) fn legacy(
    read_module: Option<&'static str>,
    write_module: Option<&'static str>,
    read_access_level: Option<&'static str>,
    write_access_level: Option<&'static str>,
    selector: i64,
    disabled: bool,
    overrides_parent: bool,
) -> LegacyPermissions {
    LegacyPermissions {
        read_module,
        write_module,
        read_access_level,
        write_access_level,
        selector,
        disabled,
        overrides_parent,
    }
}
