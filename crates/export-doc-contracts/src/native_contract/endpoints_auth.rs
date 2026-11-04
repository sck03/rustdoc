//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;
use std::collections::BTreeMap;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    ep("post","/api/auth/login","Login",super::responses::json(r("ApiLoginResponse"),&["400","401","429","503"],&[]),super::policies::rule((false,true,false),None,&[],Access::Bypass{bypass:true},true)).security(vec![BTreeMap::from([("DesktopAccess",vec![])])]).parameters(vec![p("X-ExportDocManager-Bootstrap-Token","header",None,s())]).body(true,BTreeMap::from([("application/json",Media{schema:r("ApiLoginRequest")})])),
    route("get","/api/auth/me","getCurrentUser",super::responses::json(r("ApiUserDto"),&[],&[]),super::policies::rule((true,true,false),None,&[],Access::Bypass{bypass:true},true),None,None),
    route("post","/api/auth/renew","RenewSession",super::responses::json(r("ApiLoginResponse"),&[],&[]),super::policies::rule((true,true,false),None,&[],Access::Bypass{bypass:true},true),None,None),
    route("post","/api/auth/logout","Logout",super::responses::json(r("ApiLogoutResponse"),&[],&[]),super::policies::rule((true,true,false),None,&[],Access::Bypass{bypass:true},true),None,None),
    route("post","/api/auth/unit-context","SwitchUnitContext",super::responses::json(r("ApiLoginResponse"),&[],&[]),super::policies::rule((true,true,false),None,&[],Access::Bypass{bypass:true},true),None,Some(r("UnitContextSelection"))),
] }
