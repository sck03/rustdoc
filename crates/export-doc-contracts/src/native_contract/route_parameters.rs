//! Shared query/path shapes. Requiredness, integer widths and bounds remain explicit.
use super::model::*;
pub(super) fn general_request_page() -> Vec<Parameter> {
    let mut parameters = office_request_page();
    parameters.push(p("definitionKey", "query", None, s().max_length(80)));
    parameters
}

pub(super) fn single_window_source_id() -> Vec<Parameter> {
    vec![p(
        "sourceId",
        "path",
        Some(true),
        i().minimum(num("1")).format("int64"),
    )]
}

pub(super) fn expected_revision() -> Vec<Parameter> {
    vec![p(
        "expectedRevision",
        "query",
        Some(true),
        i().minimum(num("0")).format("int64"),
    )]
}

pub(super) fn required_versioned_record_id() -> Vec<Parameter> {
    let mut parameters = record_id32();
    parameters.push(p(
        "expectedVersion",
        "query",
        Some(true),
        i().minimum(num("1")).format("int64"),
    ));
    parameters
}

pub(super) fn row_versioned_record_id() -> Vec<Parameter> {
    let mut parameters = record_id32();
    parameters.push(p("rowVersion", "query", Some(true), s().min_length(1)));
    parameters
}

#[rustfmt::skip]
pub(super) fn positive_record_id32() -> Vec<Parameter> { vec![p("id","path",Some(true),i().minimum(num("1")).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn record_id32() -> Vec<Parameter> { vec![p("id","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn versioned_record_id() -> Vec<Parameter> { vec![p("id","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("expectedVersion","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn status_keyword_page() -> Vec<Parameter> { vec![p("keyword","query",None,s()),p("status","query",None,s()),p("pageNumber","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("pageSize","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn import_file_name() -> Vec<Parameter> { vec![p("fileName","query",None,s())] }
#[rustfmt::skip]
pub(super) fn download_token() -> Vec<Parameter> { vec![p("token","path",Some(true),s())] }
#[rustfmt::skip]
pub(super) fn code() -> Vec<Parameter> { vec![p("code","path",Some(true),s())] }
#[rustfmt::skip]
pub(super) fn job_id() -> Vec<Parameter> { vec![p("jobId","path",Some(true),s())] }
#[rustfmt::skip]
pub(super) fn keyword() -> Vec<Parameter> { vec![p("keyword","query",None,s())] }
#[rustfmt::skip]
pub(super) fn required_keyword_page() -> Vec<Parameter> { vec![p("pageNumber","query",Some(true),Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("pageSize","query",Some(true),Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("keyword","query",None,s())] }
#[rustfmt::skip]
pub(super) fn keyword_page() -> Vec<Parameter> { vec![p("keyword","query",None,s()),p("pageNumber","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("pageSize","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn positive_record_id64() -> Vec<Parameter> { vec![p("id","path",Some(true),i().minimum(num("1")).format("int64"))] }
#[rustfmt::skip]
pub(super) fn history_page32() -> Vec<Parameter> { vec![p("id","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("pageNumber","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("pageSize","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn history_page64() -> Vec<Parameter> { vec![p("id","path",Some(true),i().minimum(num("1")).format("int64")),p("pageNumber","query",None,i().minimum(num("1"))),p("pageSize","query",None,i().minimum(num("1")))] }
#[rustfmt::skip]
pub(super) fn attachment_id64() -> Vec<Parameter> { vec![p("id","path",Some(true),i().minimum(num("1")).format("int64")),p("attachmentId","path",Some(true),i().minimum(num("1")).format("int64"))] }
#[rustfmt::skip]
pub(super) fn office_request_page() -> Vec<Parameter> { vec![p("pageNumber","query",None,i().minimum(num("1"))),p("pageSize","query",None,i().minimum(num("1"))),p("status","query",None,s()),p("mineOnly","query",None,b()),p("approvalsOnly","query",None,b()),p("handlingOnly","query",None,b())] }
#[rustfmt::skip]
pub(super) fn submission_id64() -> Vec<Parameter> { vec![p("id","path",Some(true),i().minimum(num("1")).format("int64")),p("submissionId","path",Some(true),i().minimum(num("1")).format("int64"))] }
#[rustfmt::skip]
pub(super) fn workflow_candidates_cursor() -> Vec<Parameter> { vec![p("id","path",Some(true),i().minimum(num("1")).format("int64")),p("beforeId","query",Some(false),i().minimum(num("1")).format("int64"))] }
#[rustfmt::skip]
pub(super) fn command_key() -> Vec<Parameter> { vec![p("id","path",Some(true),i().minimum(num("1")).format("int64")),p("commandKey","path",Some(true),s().min_length(8).max_length(128))] }
#[rustfmt::skip]
pub(super) fn decisions_page() -> Vec<Parameter> { vec![p("id","path",Some(true),i().minimum(num("1")).format("int64")),p("pageNumber","query",None,i().minimum(num("1"))),p("pageSize","query",None,i().minimum(num("1"))),p("submissionNumber","query",None,i().minimum(num("1"))),p("generation","query",None,i().minimum(num("1")))] }
#[rustfmt::skip]
pub(super) fn company_code() -> Vec<Parameter> { vec![p("companyCode","query",None,s().max_length(50))] }
#[rustfmt::skip]
pub(super) fn unit_directory_page() -> Vec<Parameter> { vec![p("unitId","query",None,s().pattern("^[1-9][0-9]{0,18}$")),p("pageNumber","query",None,i().minimum(num("1"))),p("pageSize","query",None,i().minimum(num("1")).maximum(num("100")))] }
#[rustfmt::skip]
pub(super) fn invoice_id32() -> Vec<Parameter> { vec![p("invoiceId","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn payment_id() -> Vec<Parameter> { vec![p("paymentId","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn supplier_id() -> Vec<Parameter> { vec![p("supplierId","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn supplier_link_id() -> Vec<Parameter> { vec![p("supplierId","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("id","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
#[rustfmt::skip]
pub(super) fn versioned_supplier_link_id() -> Vec<Parameter> { vec![p("supplierId","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("id","path",Some(true),i().format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),p("expectedVersion","query",None,Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))] }
