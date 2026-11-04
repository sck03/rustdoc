//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;
use std::collections::BTreeMap;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    ep("post","/api/system/shutdown-maintenance","RunShutdownMaintenance",super::responses::json(r("ApiShutdownMaintenanceResponse"),&["403"],&[]),super::policies::rule((false,true,false),None,&[],Access::Bypass{bypass:true},true)).security(vec![BTreeMap::from([("DesktopAccess",vec![])])]),
    route("post","/api/system/logs/cleanup","CleanupSystemLogs",super::responses::json(r("ApiSystemLogCleanupResponse"),&["403","409"],&["401"]),super::policies::rule((true,true,true),None,&[],Access::Bypass{bypass:true},true),None,None),
    route("get","/api/system/license","GetLicenseStatus",super::responses::json(r("ApiLicenseStatusResponse"),&[],&[]),super::policies::rule((true,true,false),None,&[],Access::Bypass{bypass:true},true),None,None),
    route("post","/api/system/license/register","RegisterLicense",super::responses::json(r("ApiLicenseRegisterResponse"),&["400","403"],&[]),super::policies::rule((true,true,false),None,&[],Access::Bypass{bypass:true},true),None,Some(r("ApiLicenseRegisterRequest"))),
    route("get","/api/system/data-maintenance/invoices/{id}","GetInvoiceDataMaintenancePreview",super::responses::json(r("ApiInvoiceDataMaintenancePreviewResponse"),&["400","403"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("document.invoices"),None,None,None,0,false,false)),&[],super::policies::access("All",&[("document.invoices",&["view"],false)]),false),Some(super::route_parameters::record_id32()),None),
    route("post","/api/system/data-maintenance/invoices/{id}/purge","PurgeCancelledInvoice",super::responses::json(r("ApiInvoicePurgeResponse"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),Some(super::policies::legacy(Some("document.invoices"),None,None,None,0,false,false)),&[],super::policies::access("All",&[("document.invoices",&["operate"],false)]),false),Some(super::route_parameters::record_id32()),Some(r("ApiInvoicePurgeRequest"))),
] }
