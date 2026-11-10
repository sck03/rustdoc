//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/worklist","GetWorklist",super::responses::json(r("WorklistPage"),&[],&["400"]),super::policies::rule((true,true,true),None,&[],Access::Bypass{bypass:true},true),Some(vec![p("includeSummary","query",None,b().default_value(serde_json::json!(true))),p("cursor","query",None,s().max_length(1024)),p("source","query",None,s()),p("due","query",None,r("WorklistDueFilter")),p("pageNumber","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("pageSize","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]),None),
] }
