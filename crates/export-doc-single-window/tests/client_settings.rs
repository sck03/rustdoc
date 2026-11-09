use export_doc_single_window::client_settings::directories;

fn task(id: &str) -> String {
    format!(
        r#"<SubFormSetting FormId="{id}" OutBox="F:\发送&amp;申报" SentBox="E:\归档" InBox="D:\回执" FailBox="F:\失败" />"#
    )
}
fn config(tasks: &str) -> String {
    format!(
        "<Settings><FormSettings><FormSetting>{tasks}</FormSetting></FormSettings><UserPin>SECRET</UserPin><EncryptorSetting Password=\"SECRET\"/></Settings>"
    )
}
#[test]
fn reads_independent_paths_and_ignores_other_tasks_and_credentials() {
    let value = directories(config(&(task("PCOOIMP") + &task("ACD") + &task("COOIMP"))).as_bytes())
        .unwrap();
    assert_eq!(value["customsCooDirectories"]["outBox"], "F:\\发送&申报");
    assert_eq!(value["agentConsignmentDirectories"]["inBox"], "D:\\回执");
    assert!(!value.to_string().contains("SECRET"));
    assert_eq!(value.as_object().unwrap().len(), 2);
    assert!(
        directories(config(&task("PCOOIMP")).as_bytes()).unwrap()["customsCooDirectories"]
            .is_null()
    );
}
#[test]
fn rejects_ambiguous_incomplete_and_malformed_settings_without_echoing_secrets() {
    for xml in [
        config(&(task("ACD") + &task("ACD"))),
        config("<SubFormSetting FormId=\"ACD\" OutBox=\"SECRET\"/>"),
        "<!DOCTYPE Settings [<!ENTITY secret 'SECRET'>]><Settings/>".into(),
        "<Settings><SECRET></Settings>".into(),
        "<Settings/><Settings/>".into(),
        "<Settings>".into(),
        "SECRET".into(),
    ] {
        let error = directories(xml.as_bytes()).unwrap_err();
        assert!(!error.contains("SECRET"));
    }
}
