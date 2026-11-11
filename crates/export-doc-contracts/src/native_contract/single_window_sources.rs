//! Independent declaration sources retain COO data separately from invoice fields.
use super::model::*;
use serde_json::json;
use std::collections::BTreeMap;

pub(super) fn schemas() -> Vec<(&'static str, Schema)> {
    vec![
        (
            "ApiSingleWindowSourceCreateRequest",
            o(vec![
                ("referenceNo", s().min_length(1).max_length(100)),
                ("invoice", r("ApiInvoiceDetailDto")),
                ("coo", r("ApiSingleWindowCooImportDto")),
            ])
            .required(vec!["referenceNo"]),
        ),
        (
            "ApiSingleWindowExcelImportPreviewResponse",
            o(vec![
                ("result", r("ApiExcelImportPreviewResponse")),
                ("coo", r("ApiSingleWindowCooImportDto")),
            ])
            .required(vec!["result"]),
        ),
        (
            "ApiSingleWindowCooImportDto",
            o(vec![
                ("certType", s().max_length(2)),
                ("transDetails", s().max_length(630)),
                ("producer", s().max_length(1000)),
                (
                    "items",
                    a(r("ApiSingleWindowCooImportItemDto")).max_items(5000),
                ),
            ])
            .required(vec!["items"]),
        ),
        (
            "ApiSingleWindowCooImportItemDto",
            o(vec![
                ("fobValue", s().max_length(25)),
                ("oriCriteria", s().max_length(20)),
                ("oriCriteriaRef", s().max_length(10)),
                ("oriCriteriaSub", s().max_length(1)),
                ("goodsDesc", s().max_length(1000)),
                ("ciqRegNo", s().max_length(10)),
                ("prdcEtpsName", s().max_length(400)),
                ("prdcEtpsConcEr", s().max_length(20)),
                ("prdcEtpsTel", s().max_length(20)),
                ("producer", s().max_length(1000)),
                ("producerTel", s().max_length(50)),
                ("producerFax", s().max_length(50)),
                ("producerEmail", s().max_length(50)),
                (
                    "producerSertFlag",
                    s().enumeration(Enumeration::Values(vec![json!(""), json!("N"), json!("Y")])),
                ),
            ]),
        ),
        (
            "ApiSingleWindowSourceDto",
            o(vec![
                ("id", i().format("int64")),
                ("invoiceNo", s()),
                ("sourceKind", s()),
                ("createdAt", s().format("date-time")),
            ])
            .required(vec!["id", "invoiceNo", "sourceKind", "createdAt"]),
        ),
        (
            "ApiSingleWindowSourceListResponse",
            o(vec![
                ("items", a(r("ApiSingleWindowSourceDto"))),
                ("totalCount", i().format("int64")),
            ])
            .required(vec!["items", "totalCount"]),
        ),
    ]
}

fn policy(action: &'static str) -> Policy {
    super::policies::rule(
        (true, true, true),
        Some(super::policies::legacy(
            Some("document.single-window"),
            None,
            None,
            None,
            0,
            false,
            false,
        )),
        &[],
        super::policies::access("All", &[("document.single-window", &[action], false)]),
        false,
    )
}

fn template_policy() -> Policy {
    let mut policy = policy("operate");
    policy.requirements = vec![
        Requirement {
            resource_key: "document.jobs",
            action: "view",
        },
        Requirement {
            resource_key: "document.jobs",
            action: "operate",
        },
    ];
    policy.access = super::policies::access(
        "All",
        &[
            ("document.single-window", &["operate"], false),
            ("document.jobs", &["view"], false),
            ("document.jobs", &["operate"], false),
        ],
    );
    policy
}

pub(super) fn endpoints() -> Vec<Endpoint> {
    vec![
        route(
            "post",
            "/api/single-window/sources/excel/template/download",
            "StartSingleWindowExcelTemplateDownloadJob",
            super::responses::reply(
                "202",
                "application/json",
                Some(r("BackgroundJobSnapshot")),
                &[],
                &["401", "403", "503"],
            ),
            template_policy(),
            None,
            None,
        ),
        route(
            "post",
            "/api/single-window/sources/excel/template/save-to-path",
            "StartSingleWindowExcelTemplateSaveToPathJob",
            super::responses::reply(
                "202",
                "application/json",
                Some(r("BackgroundJobSnapshot")),
                &[],
                &["400", "401", "403", "503"],
            ),
            template_policy(),
            None,
            Some(r("ApiExcelOutputRequest")),
        ),
        route(
            "get",
            "/api/single-window/sources",
            "ListSingleWindowSources",
            super::responses::json(r("ApiSingleWindowSourceListResponse"), &[], &["401", "403"]),
            policy("view"),
            Some(super::route_parameters::keyword_page()),
            None,
        ),
        route(
            "post",
            "/api/single-window/sources",
            "CreateSingleWindowSource",
            super::responses::json(
                r("ApiSingleWindowSourceDto"),
                &[],
                &["400", "401", "403", "409"],
            ),
            policy("operate"),
            None,
            Some(r("ApiSingleWindowSourceCreateRequest")),
        ),
        ep(
            "post",
            "/api/single-window/sources/excel/preview",
            "PreviewSingleWindowExcelImport",
            super::responses::json(
                r("ApiSingleWindowExcelImportPreviewResponse"),
                &[],
                &["400", "401", "403", "413"],
            ),
            policy("operate"),
        )
        .security(vec![BTreeMap::from([
            ("BearerAuth", vec![]),
            ("DesktopAccess", vec![]),
        ])])
        .parameters(super::route_parameters::excel_import())
        .body(
            true,
            BTreeMap::from([(
                "application/octet-stream",
                Media {
                    schema: r("IFormFile"),
                },
            )]),
        ),
    ]
}
