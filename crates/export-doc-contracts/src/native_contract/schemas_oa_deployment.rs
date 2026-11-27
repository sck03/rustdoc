use super::model::*;

fn action() -> Schema {
    s().enumeration(Enumeration::Values(vec![
        serde_json::json!("Publish"),
        serde_json::json!("Disable"),
    ]))
}
pub(super) fn entries() -> Vec<(&'static str, Schema)> {
    vec![
        (
            "OaDeploymentItem",
            o(vec![
                ("definitionKey", s().min_length(1).max_length(100)),
                ("expectedRevision", i().minimum(num("0")).format("int64")),
                ("samples", a(r("OaSimulationSample")).max_items(20)),
            ])
            .required(vec!["definitionKey"]),
        ),
        (
            "OaDeploymentPreview",
            o(vec![
                ("action", action()),
                ("items", a(r("OaDeploymentItem")).min_items(1).max_items(50)),
                ("sample", r("OaSimulationSample")),
            ])
            .required(vec!["action", "items"]),
        ),
        (
            "OaDeploymentCheck",
            o(vec![
                ("definitionKey", s()),
                ("name", s()),
                ("expectedRevision", i().format("int64")),
                ("ready", b()),
                ("samples", a(r("OaSimulationSample")).max_items(20)),
                ("failure", r("ApiErrorResponse")),
            ])
            .required(vec![
                "definitionKey",
                "name",
                "expectedRevision",
                "ready",
                "samples",
            ]),
        ),
        (
            "OaDeploymentResult",
            o(vec![
                ("expectedVersion", i().format("int64")),
                ("catalogRevision", i().format("int64")),
                ("action", action()),
                ("items", a(r("OaDeploymentCheck")).max_items(50)),
            ])
            .required(vec![
                "expectedVersion",
                "catalogRevision",
                "action",
                "items",
            ]),
        ),
        (
            "OaDeploymentApply",
            o(vec![
                ("expectedVersion", i().minimum(num("0")).format("int64")),
                ("catalogRevision", i().minimum(num("0")).format("int64")),
                ("action", action()),
                ("items", a(r("OaDeploymentItem")).min_items(1).max_items(50)),
                ("commandKey", s().min_length(8).max_length(128)),
            ])
            .required(vec![
                "expectedVersion",
                "catalogRevision",
                "action",
                "items",
                "commandKey",
            ]),
        ),
    ]
}
