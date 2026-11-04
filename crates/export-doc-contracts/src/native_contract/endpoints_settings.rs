//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/settings","GetSettings",super::responses::json(r("ApiSettingsResponse"),&[],&[]),super::policies::rule((true,true,true),None,&[],Access::Bypass{bypass:true},true),None,None),
    route("put","/api/settings","UpdateSettings",super::responses::json(r("ApiSettingsSaveResponse"),&["400","403","409"],&[]),super::policies::rule((true,true,true),None,&[("system.settings","manage")],super::policies::access("All",&[("system.settings",&["manage"],true)]),false),None,Some(r("ApiSettingsSaveRequest"))),
    route("post","/api/settings/validate","ValidateSettings",super::responses::json(r("ApiSettingsValidationResponse"),&["400","403"],&[]),super::policies::rule((true,true,true),None,&[("system.settings","manage")],super::policies::access("All",&[("system.settings",&["manage"],true)]),false),None,Some(r("ApiSettingsValidationRequest"))),
] }
