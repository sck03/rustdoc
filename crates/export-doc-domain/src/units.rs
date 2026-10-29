//! Unit hierarchy and bounded role assignments. No account, HTTP or storage access.
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Unit {
    pub code: String,
    #[serde(default)]
    pub parent_company_code: Option<String>,
    #[serde(default = "company_kind")]
    pub unit_kind: String,
    pub is_active: bool,
}
fn company_kind() -> String {
    "Company".into()
}

pub fn ancestry(units: &[Unit]) -> Result<BTreeMap<String, Vec<String>>, String> {
    let directory: BTreeMap<_, _> = units.iter().map(|u| (u.code.as_str(), u)).collect();
    if directory.len() != units.len() {
        return Err("单位代码不能重复。".into());
    }
    let mut result = BTreeMap::new();
    for unit in units {
        if !matches!(unit.unit_kind.as_str(), "Group" | "Company") {
            return Err("单位类型无效。".into());
        }
        let mut visited = BTreeSet::from([unit.code.as_str()]);
        let mut chain = vec![];
        let mut parent = unit
            .parent_company_code
            .as_deref()
            .filter(|c| !c.is_empty());
        while let Some(code) = parent {
            if !visited.insert(code) {
                return Err("单位层级不能形成循环。".into());
            }
            let ancestor = directory.get(code).ok_or("上级单位不存在。")?;
            if unit.is_active && !ancestor.is_active {
                return Err("启用单位的上级必须启用。".into());
            }
            chain.push(code.to_owned());
            if chain.len() >= 32 {
                return Err("单位层级不能超过 32 层。".into());
            }
            parent = ancestor
                .parent_company_code
                .as_deref()
                .filter(|c| !c.is_empty());
        }
        result.insert(unit.code.clone(), chain);
    }
    Ok(result)
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Management {
    #[serde(default)]
    pub analytics: bool,
    #[serde(default)]
    pub organization: bool,
    #[serde(default)]
    pub appointments: bool,
    #[serde(default)]
    pub workflows: bool,
    #[serde(default)]
    pub roles: bool,
}
impl Management {
    pub fn any(&self) -> bool {
        self.organization || self.appointments || self.workflows || self.roles || self.analytics
    }
    pub fn contains(&self, requested: &Self) -> bool {
        (!requested.analytics || self.analytics)
            && (!requested.organization || self.organization)
            && (!requested.workflows || self.workflows)
            && (!requested.appointments || self.appointments)
            && (!requested.roles || self.roles)
    }
}

/// Business grants and delegation ceilings share the existing permission vocabulary.
pub fn validate_grants(
    grants: &[crate::permissions::Grant],
) -> Result<Vec<crate::permissions::Grant>, String> {
    let normalized = crate::permissions::profile::assignable(grants)?;
    if normalized.iter().any(|g| {
        g.resource_key.starts_with("system.")
            || g.resource_key.starts_with("organization.")
            || g.data_scope == "all"
    }) {
        return Err("单位权限不能包含平台管理、转授权或全局数据范围。".into());
    }
    Ok(normalized)
}
pub fn within_ceiling(
    requested: &[crate::permissions::Grant],
    ceiling: &[crate::permissions::Grant],
) -> bool {
    requested.iter().all(|g| {
        ceiling.iter().any(|c| {
            c.resource_key == g.resource_key
                && c.action == g.action
                && crate::permissions::scope_rank(&c.data_scope)
                    >= crate::permissions::scope_rank(&g.data_scope)
        })
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn nested_units_reject_cycles_and_disabled_ancestors() {
        let make = |code: &str, parent: Option<&str>| Unit {
            code: code.into(),
            parent_company_code: parent.map(Into::into),
            unit_kind: "Company".into(),
            is_active: true,
        };
        let mut units = vec![
            make("HQ", None),
            make("A", Some("HQ")),
            make("B", Some("A")),
        ];
        assert_eq!(ancestry(&units).unwrap()["B"], vec!["A", "HQ"]);
        units[0].parent_company_code = Some("B".into());
        assert!(ancestry(&units).is_err());
        units[0].parent_company_code = None;
        units[1].is_active = false;
        assert!(ancestry(&units).is_err());
    }
    #[test]
    fn delegation_cannot_expand_actions_or_scope() {
        let grant = |scope: &str| crate::permissions::Grant {
            resource_key: "office.leave".into(),
            action: "view".into(),
            data_scope: scope.into(),
        };
        assert!(within_ceiling(&[grant("own")], &[grant("company")]));
        assert!(!within_ceiling(&[grant("company")], &[grant("own")]));
        assert!(validate_grants(&[grant("all")]).is_err());
        assert!(!Management::default().contains(&Management {
            roles: true,
            ..Default::default()
        }));
    }
}
