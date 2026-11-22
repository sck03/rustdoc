use super::model::*;

pub(super) fn schemas() -> Vec<(&'static str, Schema)> {
    vec![
        (
            "ApiHsCodeImportFieldChangeDto",
            o(vec![("field", s()), ("before", s()), ("after", s())])
                .required(vec!["field", "before", "after"]),
        ),
        (
            "ApiPagedResponseOfApiHsCodeImportPreviewItemDto",
            super::schema_shapes::paged_response("ApiHsCodeImportPreviewItemDto"),
        ),
    ]
}

pub(super) fn endpoints() -> Vec<Endpoint> {
    vec![route(
        "get",
        "/api/master-data/hs-codes/import-previews/{token}/items",
        "ListHsCodeImportPreviewItems",
        super::responses::secured_json(r("ApiPagedResponseOfApiHsCodeImportPreviewItemDto")),
        super::policies::action("document.hs-knowledge", "manage"),
        Some(vec![
            p("token", "path", Some(true), s().pattern("^[a-f0-9]{32}$")),
            p(
                "changeType",
                "query",
                None,
                s().enumeration(Enumeration::Values(
                    vec![
                        "Add",
                        "Update",
                        "Unchanged",
                        "SuspectedObsolete",
                        "Conflict",
                        "Invalid",
                    ]
                    .into_iter()
                    .map(serde_json::Value::from)
                    .collect(),
                )),
            ),
            p(
                "pageNumber",
                "query",
                None,
                i().format("int32").minimum(num("1")),
            ),
            p(
                "pageSize",
                "query",
                None,
                i().format("int32").minimum(num("1")).maximum(num("200")),
            ),
        ]),
        None,
    )]
}
