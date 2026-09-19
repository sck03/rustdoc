use ego_tree::NodeRef;
use scraper::{Html, Node};
use std::collections::{BTreeMap, BTreeSet};

const ALLOWED: &[&str] = &[
    "a",
    "b",
    "blockquote",
    "br",
    "div",
    "em",
    "h1",
    "h2",
    "h3",
    "h4",
    "hr",
    "i",
    "li",
    "ol",
    "p",
    "s",
    "span",
    "strong",
    "table",
    "tbody",
    "td",
    "tfoot",
    "th",
    "thead",
    "tr",
    "u",
    "ul",
];
const BLOCKED: &[&str] = &[
    "audio", "base", "button", "canvas", "embed", "form", "frame", "frameset", "iframe", "input",
    "link", "math", "meta", "object", "option", "script", "select", "source", "style", "svg",
    "textarea", "video",
];
pub const VARIABLES: &[(&str, &str, &str)] = &[
    ("CustomerName", "客户名称", "Acme Trading"),
    ("ContactName", "联系人", "Alice"),
    ("CompanyName", "本公司名称", "示例外贸有限公司"),
    ("ProductName", "产品名称", "Sample Product"),
    ("QuotationNo", "报价单号", "QT-20260917-001"),
    ("SenderName", "发件人姓名", "业务员"),
    ("Today", "当前日期", "2026-09-17"),
];
pub fn escape(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&#39;")
}
pub fn plain_to_html(value: &str) -> String {
    format!(
        "<p>{}</p>",
        escape(value).replace("\r\n", "\n").replace('\n', "<br>")
    )
}

fn visit(
    node: NodeRef<'_, Node>,
    html: &mut String,
    plain: &mut String,
    depth: usize,
) -> Result<(), String> {
    if depth > 256 {
        return Err("邮件正文嵌套层级过深。".into());
    }
    if let Node::Text(text) = node.value() {
        html.push_str(&escape(text));
        plain.push_str(text);
        return Ok(());
    }
    let element = node.value().as_element();
    let name = element.map(|e| e.name()).unwrap_or("");
    if BLOCKED.contains(&name) {
        return Ok(());
    }
    let allowed = ALLOWED.contains(&name);
    if allowed {
        html.push('<');
        html.push_str(name);
        if let Some(element) = element {
            for (key, value) in element.attrs() {
                let safe = match (name, key) {
                    ("a", "href") => {
                        let lower = value.trim().to_lowercase();
                        !value.chars().any(char::is_control)
                            && (lower.starts_with('#')
                                || ["https://", "http://", "mailto:", "tel:"]
                                    .iter()
                                    .any(|p| lower.starts_with(p)))
                    }
                    ("a", "title") => true,
                    ("td" | "th", "colspan" | "rowspan") => {
                        value.parse::<u16>().is_ok_and(|n| (1..=100).contains(&n))
                    }
                    ("td" | "th", "scope") => {
                        ["row", "col", "rowgroup", "colgroup"].contains(&value)
                    }
                    _ => false,
                };
                if safe {
                    html.push(' ');
                    html.push_str(key);
                    html.push_str("=\"");
                    html.push_str(&escape(value.trim()));
                    html.push('"');
                }
            }
            if name == "a"
                && element
                    .attr("target")
                    .is_some_and(|t| t.eq_ignore_ascii_case("_blank"))
            {
                html.push_str(" target=\"_blank\" rel=\"noopener noreferrer\"");
            }
        }
        html.push('>');
        if name == "br" || name == "hr" {
            plain.push('\n');
        }
    }
    for child in node.children() {
        visit(child, html, plain, depth + 1)?;
    }
    if allowed && !["br", "hr"].contains(&name) {
        html.push_str("</");
        html.push_str(name);
        html.push('>');
        if ["p", "div", "li", "tr", "h1", "h2", "h3", "h4", "blockquote"].contains(&name)
            && !plain.ends_with('\n')
        {
            plain.push('\n');
        }
        if ["td", "th"].contains(&name) {
            plain.push('\t');
        }
    }
    Ok(())
}
pub fn sanitize(value: &str) -> Result<(String, String), String> {
    if value.len() > 2 * 1024 * 1024 {
        return Err("邮件正文超过 2 MiB。".into());
    }
    let document = Html::parse_fragment(value);
    let mut html = String::new();
    let mut plain = String::new();
    visit(document.tree.root(), &mut html, &mut plain, 0)?;
    if html.len() > 4 * 1024 * 1024 {
        return Err("邮件正文展开后超过容量上限。".into());
    }
    Ok((html.trim().into(), plain.trim().into()))
}
pub struct Preview {
    pub subject: String,
    pub html: String,
    pub unresolved: Vec<String>,
}
pub fn preview(
    subject: &str,
    body: &str,
    variables: &BTreeMap<String, String>,
) -> Result<Preview, String> {
    if subject.chars().count() > 300
        || body.chars().count() > 10000
        || variables.len() > 50
        || variables.values().any(|v| v.chars().count() > 4000)
    {
        return Err("邮件模板或变量超过允许长度。".into());
    }
    let (mut html, _) = sanitize(body)?;
    let mut subject = subject.to_owned();
    for (key, _, _) in VARIABLES {
        let token = format!("{{{{{key}}}}}");
        let value = variables.get(*key).map(String::as_str).unwrap_or("");
        subject = subject.replace(&token, value);
        html = html.replace(&token, &escape(value));
    }
    if subject.contains(['\r', '\n']) {
        return Err("邮件主题不能包含换行。".into());
    }
    let mut unresolved = BTreeSet::new();
    for source in [&subject, &html] {
        for suffix in source.split("{{").skip(1) {
            if let Some((token, _)) = suffix.split_once("}}") {
                if token
                    .as_bytes()
                    .first()
                    .is_some_and(u8::is_ascii_alphabetic)
                    && token.bytes().all(|b| b.is_ascii_alphanumeric())
                {
                    unresolved.insert(format!("{{{{{token}}}}}"));
                }
            }
        }
    }
    Ok(Preview {
        subject,
        html,
        unresolved: unresolved.into_iter().collect(),
    })
}
