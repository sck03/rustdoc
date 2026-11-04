//! Typed route catalogue; common authentication, parameters and JSON media are composed once.
use super::model::*;

#[rustfmt::skip]
pub(super) fn entries() -> Vec<Endpoint> { vec![
    route("get","/api/diagnostics/metrics","GetRuntimeMetrics",Responses::from([("200",Response{description:"Process counters and retained task counts; no business data.",content:Some(Content::from([("application/json",Media{schema:r("ApiRuntimeMetricsResponse")})]))}),("401",Response{description:"Authentication required",content:None}),("403",Response{description:"Administrator required",content:None}),("503",Response{description:"Dependency unavailable",content:None})]),super::policies::rule((true,true,false),None,&[("system.settings","view")],super::policies::access("All",&[("system.settings",&["view"],true)]),false),None,None),
] }
