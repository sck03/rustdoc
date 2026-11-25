//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;
use std::collections::BTreeMap;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/server-migration/status","GetServerMigrationStatus",super::responses::json(r("ApiServerMigrationStatusResponse"),&[],&["401","403"]),super::policies::rule((true,true,true),super::policies::access("All",&[("system.disaster-recovery",&["manage"],true)])),None,None),
    route("post","/api/server-migration/authorization","AuthorizeServerMigrationOperation",super::responses::json(r("ApiSensitiveOperationAuthorizationResponse"),&[],&["400","401","403","429"]),super::policies::rule((true,true,true),super::policies::access("All",&[("system.disaster-recovery",&["manage"],true)])),None,Some(r("ApiSensitiveOperationAuthorizationRequest"))),
    route("post","/api/server-migration/packages","CreateServerMigrationPackage",super::responses::reply("202","application/json",Some(r("BackgroundJobSnapshot")),&[],&["400","401","403","429"]),super::policies::rule((true,true,true),super::policies::access("All",&[("system.disaster-recovery",&["manage"],true)])),None,Some(r("ApiServerMigrationCreateRequest"))),
    ep("post","/api/server-migration/restore","StageServerMigrationRestore",super::responses::reply("202","application/json",Some(r("BackgroundJobSnapshot")),&[],&["400","401","403","409","413","429","503"]),super::policies::rule((true,true,true),super::policies::access("All",&[("system.disaster-recovery",&["manage"],true)]))).security(vec![BTreeMap::from([("BearerAuth",vec![]),("DesktopAccess",vec![])])]).parameters(vec![p("X-ExportDocManager-Restore-Confirmation","header",Some(true),s()),p("X-ExportDocManager-Sensitive-Operation-Ticket","header",Some(true),s()),p("X-ExportDocManager-Migration-Password","header",Some(true),s()),p("X-ExportDocManager-Migration-File-Name","header",Some(true),s())]).body(true,BTreeMap::from([("application/octet-stream",Media{schema:r("IFormFile")})])),
] }
