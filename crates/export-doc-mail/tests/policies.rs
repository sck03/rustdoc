use export_doc_mail::{attachment, content, recipient};

#[test]
fn recipient_policy_checks_exact_domain_boundaries_and_block_wins() {
    let user = recipient::mailbox("业务员 <sales@sub.example.com>").unwrap();
    assert!(recipient::allowed(&user, "example.com", "").unwrap());
    assert!(!recipient::allowed(&user, "example.com", "sub.example.com").unwrap());
    let attacker = recipient::mailbox("sales@notexample.com").unwrap();
    assert!(!recipient::allowed(&attacker, "example.com", "").unwrap());
    assert!(recipient::mailbox("a@example.com,b@example.com").is_err());
    assert!(recipient::mailbox("a@example.com\r\nBcc:b@example.com").is_err());
    assert_eq!(
        recipient::normalize_rules("*.Example.com;EXAMPLE.COM\n@sub.example.com").unwrap(),
        "example.com\nsub.example.com"
    );
    assert!(recipient::normalize_rules("https://example.com").is_err());
}
#[test]
fn html_keeps_business_formatting_but_removes_active_content_and_unsafe_urls() {
    let (html,plain)=content::sanitize("<p onclick='alert(1)'>Hello <b>世界</b><br>next</p><script>secret()</script><svg onload='run()'><text>payload</text></svg><a href='jav&#97;script:run()' target='_blank'>link</a><table><tr><td colspan='2'>cell</td></tr></table>").unwrap();
    assert!(html.contains("<b>世界</b>"));
    assert!(html.contains("colspan=\"2\""));
    assert!(!html.contains("script"));
    assert!(!html.contains("onclick"));
    assert!(!html.contains("payload"));
    assert!(!html.contains("href="));
    assert!(html.contains("noopener noreferrer"));
    assert!(plain.contains("Hello 世界\nnext"));
    let preview = content::preview(
        "你好 {{CustomerName}}",
        "<p>{{CustomerName}} {{Unknown}}</p>",
        &[("CustomerName".into(), "<img src=x onerror=run()>".into())].into(),
    )
    .unwrap();
    assert!(!preview.html.contains("<img"));
    assert!(preview.html.contains("&lt;img"));
    assert_eq!(preview.unresolved, vec!["{{Unknown}}"]);
}
#[test]
fn attachment_type_and_capacity_are_checked_without_opening_host_paths() {
    assert_eq!(
        attachment::inspect("note.txt", "中文备注".as_bytes()).unwrap(),
        "text/plain"
    );
    assert!(attachment::inspect("wrong.pdf", b"not a pdf").is_err());
    assert!(attachment::inspect("run.exe", b"MZ").is_err());
    assert!(attachment::inspect("fake.docx", b"PKfake").is_err());
    assert!(attachment::inspect("binary.csv", b"a\0b").is_err());
}
