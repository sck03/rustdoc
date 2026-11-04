//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/business-attachment-categories","ListBusinessAttachmentCategories",super::responses::json(r("BusinessAttachmentCategoryCatalog"),&[],&[]),super::policies::action("document.invoices","view"),Some(vec![p("invoiceId","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]),None),
    route("post","/api/business-attachment-categories","CreateBusinessAttachmentCategory",super::responses::json(r("BusinessAttachmentCategoryRecord"),&[],&[]),super::policies::action("document.invoices","manage"),None,Some(r("BusinessAttachmentCategoryCreate"))),
    route("put","/api/business-attachment-categories/{id}","UpdateBusinessAttachmentCategory",super::responses::json(r("BusinessAttachmentCategoryRecord"),&[],&[]),super::policies::action("document.invoices","manage"),Some(super::route_parameters::positive_record_id32()),Some(r("BusinessAttachmentCategoryUpdate"))),
    route("delete","/api/business-attachment-categories/{id}","DeleteBusinessAttachmentCategory",super::responses::json(r("ApiCommandResponse"),&[],&[]),super::policies::action("document.invoices","manage"),Some(vec![p("id","path",Some(true),i().minimum(num("1")).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("expectedVersion","query",Some(true),Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]),None),
] }
