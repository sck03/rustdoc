//! Independent declaration sources share the invoice import projection, not invoice persistence.
use super::model::*;
use std::collections::BTreeMap;

pub(super) fn schemas() -> Vec<(&'static str, Schema)> {
    vec![
        (
            "ApiSingleWindowSourceCreateRequest",
            o(vec![
                ("referenceNo", s().min_length(1).max_length(100)),
                ("invoice", r("ApiInvoiceDetailDto")),
            ])
            .required(vec!["referenceNo"]),
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

pub(super) fn endpoints() -> Vec<Endpoint> {
    vec![
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
                r("ApiExcelImportPreviewResponse"),
                &[],
                &["400", "401", "403", "413"],
            ),
            policy("operate"),
        )
        .security(vec![BTreeMap::from([
            ("BearerAuth", vec![]),
            ("DesktopAccess", vec![]),
        ])])
        .parameters(super::route_parameters::import_file_name())
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
