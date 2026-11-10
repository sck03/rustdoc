//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    read("/api/email-templates","ListEmailTemplates","ApiPagedResponseOfApiEmailTemplateDto",catalog_parameters(),false),
    route("post","/api/email-templates","CreateEmailTemplate",super::responses::reply("201","application/json",Some(r("ApiEmailTemplateDto")),&["400","403","409"],&[]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","edit")],super::policies::access("All",&[("sales.email-templates",&["edit"],false)]),false),None,Some(r("ApiEmailTemplateDraftRequest"))),
    route("get","/api/email-templates/variables","ListEmailTemplateVariables",super::responses::json(a(r("ApiEmailTemplateVariableDto")),&["403"],&[]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","view")],super::policies::access("All",&[("sales.email-templates",&["view"],false)]),false),None,None),
    route("post","/api/email-templates/preview","PreviewEmailTemplate",super::responses::json(r("ApiEmailTemplatePreviewDto"),&["400","403"],&[]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","view")],super::policies::access("All",&[("sales.email-templates",&["view"],false)]),false),None,Some(r("ApiEmailTemplatePreviewRequest"))),
    route("put","/api/email-templates/{id}/draft","SaveEmailTemplateDraft",super::responses::json(r("ApiEmailTemplateDto"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","edit")],super::policies::access("All",&[("sales.email-templates",&["edit"],false)]),false),Some(super::route_parameters::record_id32()),Some(r("ApiEmailTemplateDraftRequest"))),
    route("post","/api/email-templates/{id}/publish","PublishEmailTemplate",super::responses::json(r("ApiEmailTemplateDto"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","publish")],super::policies::access("All",&[("sales.email-templates",&["publish"],false)]),false),Some(super::route_parameters::record_id32()),Some(r("ApiEmailTemplateLifecycleRequest"))),
    route("post","/api/email-templates/{id}/share","ShareEmailTemplate",super::responses::json(r("ApiEmailTemplateDto"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","share")],super::policies::access("All",&[("sales.email-templates",&["share"],false)]),false),Some(super::route_parameters::record_id32()),Some(r("ApiEmailTemplateShareRequest"))),
    route("post","/api/email-templates/{id}/disable","DisableEmailTemplate",super::responses::json(r("ApiEmailTemplateDto"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","deactivate")],super::policies::access("All",&[("sales.email-templates",&["deactivate"],false)]),false),Some(super::route_parameters::record_id32()),Some(r("ApiEmailTemplateLifecycleRequest"))),
    route("post","/api/email-templates/{id}/restore","RestoreEmailTemplate",super::responses::json(r("ApiEmailTemplateDto"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","restore")],super::policies::access("All",&[("sales.email-templates",&["restore"],false)]),false),Some(super::route_parameters::record_id32()),Some(r("ApiEmailTemplateLifecycleRequest"))),
    route("delete","/api/email-templates/{id}","ArchiveEmailTemplate",super::responses::json(r("ApiEmailTemplateDto"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","delete")],super::policies::access("All",&[("sales.email-templates",&["delete"],false)]),false),Some(vec![p("id","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("expectedVersion","query",Some(true),Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]),None),
    read("/api/email-templates/{id}/versions","ListEmailTemplateVersions","ApiPagedResponseOfApiEmailTemplateVersionSummaryDto",super::route_parameters::history_page32(),true),
    read("/api/email-templates/{id}/versions/{versionNumber}","GetEmailTemplateVersion","ApiEmailTemplateVersionDto",vec![p("id","path",Some(true),i().format("int32")),p("versionNumber","path",Some(true),i().minimum(num("1")).format("int32"))],true),
    route("post","/api/email-templates/{id}/versions/{versionNumber}/restore","RestoreEmailTemplateVersion",super::responses::json(r("ApiEmailTemplateDto"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("sales.email-templates"),None,None,None,0,false,false)),&[("sales.email-templates","restore")],super::policies::access("All",&[("sales.email-templates",&["restore"],false)]),false),Some(vec![p("id","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("versionNumber","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]),Some(r("ApiEmailTemplateLifecycleRequest"))),
] }

fn catalog_parameters() -> Vec<Parameter> {
    let mut parameters = super::route_parameters::keyword_page();
    parameters.extend([
        p("category", "query", None, s()),
        p("includeArchived", "query", None, b()),
        p("scope", "query", None, s()),
    ]);
    parameters
}
fn read(
    path: &'static str,
    operation: &'static str,
    schema: &'static str,
    parameters: Vec<Parameter>,
    missing: bool,
) -> Endpoint {
    route(
        "get",
        path,
        operation,
        super::responses::json(
            r(schema),
            &["400", "403"],
            if missing { &["404"] } else { &[] },
        ),
        super::policies::rule(
            (true, true, true),
            Some(super::policies::legacy(
                Some("sales.email-templates"),
                None,
                None,
                None,
                0,
                false,
                false,
            )),
            &[("sales.email-templates", "view")],
            super::policies::access("All", &[("sales.email-templates", &["view"], false)]),
            false,
        ),
        Some(parameters),
        None,
    )
}
