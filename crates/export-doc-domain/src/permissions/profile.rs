//! Shared validation and resolution for groups and individual accounts.
use super::{EffectiveGrant, Grant, catalog, effective_details, normalize, resource};

pub fn assignable(grants: &[Grant]) -> Result<Vec<Grant>, String> {
    let grants = normalize(grants)?;
    if grants
        .iter()
        .any(|grant| resource(&grant.resource_key).is_some_and(|r| r.is_technical))
    {
        return Err("系统身份与技术依赖能力由服务端派生，不能直接授予。".into());
    }
    Ok(grants)
}

pub fn disabled_modules(modules: &[String]) -> Result<Vec<String>, String> {
    if modules.len() > 100 {
        return Err("关闭模块数量超出上限。".into());
    }
    let mut result = Vec::new();
    for module in modules {
        let key = module.trim().to_ascii_lowercase();
        if !catalog()
            .resources
            .iter()
            .any(|r| r.module_key == key && !r.is_technical)
        {
            return Err("未知或不可配置的功能模块。".into());
        }
        result.push(key);
    }
    result.sort();
    result.dedup();
    Ok(result)
}

pub fn resolve(grants: &[Grant], disabled: &[String]) -> Result<Vec<Grant>, String> {
    Ok(resolve_details(grants, disabled)?
        .into_iter()
        .map(|item| item.grant)
        .collect())
}

pub fn resolve_details(
    grants: &[Grant],
    disabled: &[String],
) -> Result<Vec<EffectiveGrant>, String> {
    let disabled = disabled_modules(disabled)?;
    let enabled = |grant: &Grant| {
        resource(&grant.resource_key).is_some_and(|r| !disabled.contains(&r.module_key))
    };
    let direct: Vec<_> = normalize(grants)?.into_iter().filter(enabled).collect();
    // Filter again after dependency expansion: a dependency cannot reopen a disabled module.
    Ok(effective_details(&direct)?
        .into_iter()
        .filter(|item| enabled(&item.grant))
        .collect())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn disabled_modules_cannot_be_reopened_by_output_dependencies() {
        let direct = vec![Grant {
            resource_key: "document.invoice-output".into(),
            action: "preview".into(),
            data_scope: "own".into(),
        }];
        let active = resolve(&direct, &[]).unwrap();
        assert!(active.iter().any(|g| g.resource_key == "document.invoices"));
        let restricted = resolve(&direct, &["document.invoices".into()]).unwrap();
        assert!(
            !restricted
                .iter()
                .any(|g| g.resource_key == "document.invoices")
        );
        assert!(
            restricted
                .iter()
                .any(|g| g.resource_key == "document.invoice-output")
        );
        assert!(disabled_modules(&["system.unknown".into()]).is_err());
        assert!(
            assignable(&[Grant {
                resource_key: "system.users".into(),
                action: "manage".into(),
                data_scope: "all".into(),
            }])
            .is_err(),
            "administrator permissions cannot be assigned directly"
        );
    }
}
