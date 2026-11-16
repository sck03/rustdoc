//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/custom-options/{optionType}","ListCustomOptions",super::responses::json(r("ApiCustomOptionListResponse"),&["400"],&[]),super::policies::rule((true,true,true),super::policies::access("All",&[("document.custom-options",&["view"],false)])),Some(vec![p("optionType","path",Some(true),s())]),None),
    route("post","/api/custom-options/{optionType}","SaveCustomOption",super::responses::json(r("ApiCustomOptionListResponse"),&["400"],&[]),super::policies::rule((true,true,true),super::policies::access("All",&[("document.custom-options",&["operate"],false)])),Some(vec![p("optionType","path",Some(true),s())]),Some(r("ApiCustomOptionSaveRequest"))),
] }
