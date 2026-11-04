//! Shared response envelopes. Each family retains its own null, integer and paging rules.
use super::model::*;

#[rustfmt::skip]
pub(super) fn item_collection(item: &str) -> Schema {
    o(vec![("items",a(r(item)))]).required(vec!["items"])
}

#[rustfmt::skip]
pub(super) fn code_name() -> Schema {
    o(vec![("code",s()),("name",s())]).required(vec!["code","name"])
}

#[rustfmt::skip]
pub(super) fn office_page(item: &str) -> Schema {
    o(vec![
        ("items",a(r(item))),("totalCount",i().format("int64")),("pageNumber",i().format("int64")),
        ("pageSize",i().format("int64"))
    ]).required(vec!["items","totalCount","pageNumber","pageSize"])
}

#[rustfmt::skip]
pub(super) fn lifecycle_request() -> Schema {
    o(vec![("expectedVersion",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$"))]).required(vec!["expectedVersion"])
}

#[rustfmt::skip]
pub(super) fn file_path_request() -> Schema {
    o(vec![("filePath",s())])
}

#[rustfmt::skip]
pub(super) fn paged_response(item: &str) -> Schema {
    o(vec![
        ("items",a(r(item))),("totalCount",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("pageNumber",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),
        ("pageSize",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("totalPages",Schema::default().kind(Types::Many(vec![Primitive::Integer,Primitive::String])).format("int32").pattern("^-?(?:0|[1-9]\\d*)$")),("hasPreviousPage",b()),
        ("hasNextPage",b())
    ]).required(vec!["items","totalCount","pageNumber","pageSize","totalPages","hasPreviousPage","hasNextPage"])
}

#[rustfmt::skip]
pub(super) fn identity_page(item: &str) -> Schema {
    o(vec![
        ("items",a(r(item))),("totalCount",i().minimum(num("0")).format("int64")),("pageNumber",i().minimum(num("0")).format("int64")),
        ("pageSize",i().minimum(num("0")).format("int64"))
    ]).required(vec!["items","totalCount","pageNumber","pageSize"]).additional(Additional::Allowed(false))
}

#[rustfmt::skip]
pub(super) fn counted_collection(item: &str) -> Schema {
    o(vec![("items",a(r(item)).max_items(100)),("totalCount",i().format("int64"))]).required(vec!["items","totalCount"])
}
