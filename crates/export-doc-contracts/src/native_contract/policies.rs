//! Authorization is declared once; endpoint factories preserve explicit access rules.
use super::model::*;

pub(super) fn action(key: &'static str, action: &'static str) -> Policy {
    rule(
        (true, true, true),
        access("All", &[(key, &[action], false)]),
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
pub(super) fn rule(flags: (bool, bool, bool), access: Access) -> Policy {
    Policy {
        authentication_context: None,
        requires_authentication: flags.0,
        requires_desktop_access: flags.1,
        requires_license: flags.2,
        access,
    }
}
