use lettre::{Address, message::Mailbox};
use std::collections::BTreeSet;

pub fn mailbox(value: &str) -> Result<Mailbox, String> {
    if value.chars().any(char::is_control) || value.chars().count() > 400 {
        return Err("邮箱地址无效。".into());
    }
    let mut mailbox: Mailbox = value
        .trim()
        .parse()
        .map_err(|_| "请输入一个有效的邮箱地址。")?;
    let domain = domain(mailbox.email.domain())?;
    mailbox.email = Address::new(mailbox.email.user(), domain).map_err(|_| "邮箱地址无效。")?;
    Ok(mailbox)
}
fn domain(value: &str) -> Result<String, String> {
    let value = value.trim().trim_end_matches('.');
    let host = match url::Host::parse(value).map_err(|_| "邮箱域名无效。")? {
        url::Host::Domain(domain) => domain,
        _ => return Err("邮箱域名必须是 DNS 名称。".into()),
    };
    if host.len() > 253
        || host.split('.').any(|s| {
            s.is_empty()
                || s.len() > 63
                || s.starts_with('-')
                || s.ends_with('-')
                || !s.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-')
        })
    {
        return Err("邮箱域名无效。".into());
    }
    Ok(host.to_lowercase())
}
fn rules(value: &str) -> Result<Vec<String>, String> {
    let mut parsed = BTreeSet::new();
    let mut count = 0;
    for rule in value
        .split(['\r', '\n', ',', ';'])
        .map(str::trim)
        .filter(|s| !s.is_empty())
    {
        count += 1;
        if count > 500 {
            return Err("收件人规则最多 500 条。".into());
        }
        let rule = if let Some(host) = rule
            .strip_prefix("*@")
            .or_else(|| rule.strip_prefix("*."))
            .or_else(|| rule.strip_prefix('@'))
        {
            domain(host)?
        } else if rule.contains('@') {
            mailbox(rule)?.email.to_string().to_lowercase()
        } else {
            domain(rule)?
        };
        parsed.insert(rule);
    }
    Ok(parsed.into_iter().collect())
}
pub fn normalize_rules(value: &str) -> Result<String, String> {
    rules(value).map(|rules| rules.join("\n"))
}
pub fn allowed(address: &Mailbox, allow: &str, block: &str) -> Result<bool, String> {
    let email = address.email.to_string().to_lowercase();
    let host = address.email.domain().to_lowercase();
    let matches = |rule: &String| {
        if rule.contains('@') {
            email == *rule
        } else {
            host == *rule
                || host
                    .strip_suffix(rule)
                    .is_some_and(|prefix| prefix.ends_with('.'))
        }
    };
    if rules(block)?.iter().any(matches) {
        return Ok(false);
    }
    let allow = rules(allow)?;
    Ok(allow.is_empty() || allow.iter().any(matches))
}
