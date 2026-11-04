//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("post","/api/support-package/save-to-runtime","SaveSupportPackageToRuntime",super::responses::json(r("ApiSupportPackageResponse"),&[],&["400","401","403","503"]),super::policies::rule((true,true,true),None,&[],Access::Bypass{bypass:true},true),None,Some(r("ApiSupportPackageRequest"))),
    route("post","/api/support-package/download","DownloadSupportPackage",Responses::from([("202",Response{description:"Accepted",content:Some(Content::from([("application/json",Media{schema:r("BackgroundJobSnapshot")})]))}),("400",Response{description:"Bad Request",content:None}),("401",Response{description:"Unauthorized",content:None}),("403",Response{description:"Forbidden",content:None}),("426",Response{description:"Upgrade Required",content:None}),("429",Response{description:"Too Many Requests",content:None})]),super::policies::rule((true,true,true),None,&[],Access::Bypass{bypass:true},true),None,Some(r("ApiSupportPackageRequest"))),
] }
