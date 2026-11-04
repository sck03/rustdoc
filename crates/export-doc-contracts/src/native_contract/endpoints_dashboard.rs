//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/dashboard","GetDashboard",super::responses::json(r("ApiDashboardResponse"),&[],&[]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("document.dashboard"),None,None,None,0,false,false)),&[],super::policies::access("All",&[("document.dashboard",&["view"],false)]),false),None,None),
] }
