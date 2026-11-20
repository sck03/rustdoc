//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("post","/api/system/logs/cleanup","CleanupSystemLogs",super::responses::json(r("ApiSystemLogCleanupResponse"),&["403","409"],&["401"]),super::policies::rule((true,true,true),Access::Bypass{bypass:true}),None,None),
    route("get","/api/system/license","GetLicenseStatus",super::responses::json(r("ApiLicenseStatusResponse"),&[],&[]),super::policies::rule((true,true,false),Access::Bypass{bypass:true}),None,None),
    route("post","/api/system/license/register","RegisterLicense",super::responses::json(r("ApiLicenseRegisterResponse"),&["400","403"],&[]),super::policies::rule((true,true,false),Access::Bypass{bypass:true}),None,Some(r("ApiLicenseRegisterRequest"))),
    route("get","/api/system/data-maintenance/invoices/{id}","GetInvoiceDataMaintenancePreview",super::responses::json(r("ApiInvoiceDataMaintenancePreviewResponse"),&["400","403"],&["404"]),super::policies::rule((true,true,true),super::policies::access("All",&[("document.invoices",&["view"],false)])),Some(super::route_parameters::record_id32()),None),
    route("post","/api/system/data-maintenance/invoices/{id}/purge","PurgeCancelledInvoice",super::responses::json(r("ApiInvoicePurgeResponse"),&["400","403","409"],&["404"]),super::policies::rule((true,true,true),super::policies::access("All",&[("document.invoices",&["operate"],false)])),Some(super::route_parameters::record_id32()),Some(r("ApiInvoicePurgeRequest"))),
] }
