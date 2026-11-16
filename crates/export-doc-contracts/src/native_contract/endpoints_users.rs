//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/users","ListUsers",super::responses::json(r("ApiUserListResponse"),&["403"],&[]),super::policies::rule((true,true,true),super::policies::access("All",&[("system.users",&["manage"],true)])),None,None),
    route("post","/api/users","createUserAccount",super::responses::json(r("ApiUserSaveResponse"),&["400","403","404","409"],&[]),super::policies::rule((true,true,true),super::policies::access("All",&[("system.users",&["manage"],true)])),None,Some(r("ApiUserSaveRequest"))),
    route("put","/api/users/{id}","updateUserAccount",super::responses::json(r("ApiUserSaveResponse"),&["400","403","404","409"],&[]),super::policies::rule((true,true,true),super::policies::access("All",&[("system.users",&["manage"],true)])),Some(super::route_parameters::record_id32()),Some(r("ApiUserSaveRequest"))),
    route("delete","/api/users/{id}","deleteUserAccount",super::responses::json(r("ApiCommandResponse"),&["400","403","404","409"],&[]),super::policies::rule((true,true,true),super::policies::access("All",&[("system.users",&["manage"],true)])),Some(super::route_parameters::versioned_record_id()),None),
] }
