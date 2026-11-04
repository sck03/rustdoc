//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/permission-templates","ListPermissionTemplates",super::responses::json(r("ApiPermissionTemplateCatalogResponse"),&["403"],&[]),super::policies::rule((true,true,true),None,&[("system.permissions","manage")],super::policies::access("All",&[("system.permissions",&["manage"],true)]),false),None,None),
    route("post","/api/permission-templates","CreatePermissionTemplate",super::responses::json(r("ApiPermissionTemplateDto"),&["400","403","404","409"],&[]),super::policies::rule((true,true,true),None,&[("system.permissions","manage")],super::policies::access("All",&[("system.permissions",&["manage"],true)]),false),None,Some(r("ApiPermissionTemplateSaveRequest"))),
    route("put","/api/permission-templates/{id}","UpdatePermissionTemplate",super::responses::json(r("ApiPermissionTemplateDto"),&["400","403","404","409"],&[]),super::policies::rule((true,true,true),None,&[("system.permissions","manage")],super::policies::access("All",&[("system.permissions",&["manage"],true)]),false),Some(super::route_parameters::record_id32()),Some(r("ApiPermissionTemplateSaveRequest"))),
    route("delete","/api/permission-templates/{id}","DeletePermissionTemplate",super::responses::json(r("ApiCommandResponse"),&["403","404","409"],&[]),super::policies::rule((true,true,true),None,&[("system.permissions","manage")],super::policies::access("All",&[("system.permissions",&["manage"],true)]),false),Some(super::route_parameters::versioned_record_id()),None),
] }
