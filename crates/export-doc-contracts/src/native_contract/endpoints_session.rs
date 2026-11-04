//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;
use std::collections::BTreeMap;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    ep("get","/livez","getLiveness",super::responses::empty(&["200"]),super::policies::rule((false,false,false),None,&[],super::policies::access("Any",&[]),false)),
    ep("head","/livez","getLiveness",super::responses::empty(&["200"]),super::policies::rule((false,false,false),None,&[],super::policies::access("Any",&[]),false)),
    ep("get","/readyz","getReadiness",super::responses::empty(&["200","503"]),super::policies::rule((false,false,false),None,&[],super::policies::access("Any",&[]),false)),
    ep("head","/readyz","getReadiness",super::responses::empty(&["200","503"]),super::policies::rule((false,false,false),None,&[],super::policies::access("Any",&[]),false)),
    ep("get","/healthz","getHealth",super::responses::json(r("ApiHealthResponse"),&[],&[]),super::policies::rule((false,false,false),None,&[],super::policies::access("Any",&[]),false)),
    ep("get","/downloads/postgresql-backups/{token}","DownloadPostgreSqlPhysicalBackupWithTicket",Responses::from([("200",Response{description:"OK",content:Some(Content::from([("application/octet-stream",Media{schema:Schema::default().kind(Types::One(Primitive::String)).format("byte")})]))}),("404",Response{description:"Not Found",content:None}),("426",Response{description:"Upgrade Required",content:None})]),super::policies::rule((false,true,false),None,&[("system.disaster-recovery","manage")],super::policies::access("All",&[("system.disaster-recovery",&["manage"],true)]),false)).security(vec![BTreeMap::from([("DesktopAccess",vec![])])]).parameters(super::route_parameters::download_token()),
    ep("get","/downloads/jobs/{token}","DownloadJobResultWithTicket",Responses::from([("200",Response{description:"OK",content:Some(Content::from([("application/octet-stream",Media{schema:Schema::default().kind(Types::One(Primitive::String)).format("byte")})]))}),("404",Response{description:"Not Found",content:None}),("426",Response{description:"Upgrade Required",content:None})]),super::policies::rule((false,true,false),Some(super::policies::legacy(Some("document.jobs"),None,None,None,0,false,false)),&[],Access::Bypass{bypass:true},true)).security(vec![BTreeMap::from([("DesktopAccess",vec![])])]).parameters(super::route_parameters::download_token()),
] }
