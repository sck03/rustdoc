//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/shared-database/ownership","GetSharedDatabaseOwnershipSummary",super::responses::json(r("ApiSharedDatabaseOwnershipSummaryResponse"),&[],&["401","403"]),super::policies::rule((true,true,true),None,&[],Access::Bypass{bypass:true},true),None,None),
    route("post","/api/shared-database/ownership/transfer","TransferSharedDatabaseOwnership",super::responses::json(r("ApiSharedDatabaseOwnershipTransferResponse"),&[],&["400","401","403","404","409","503"]),super::policies::rule((true,true,true),None,&[],Access::Bypass{bypass:true},true),None,Some(r("ApiSharedDatabaseOwnershipTransferRequest"))),
] }
