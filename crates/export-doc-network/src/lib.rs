mod addresses;
pub use addresses::public_address;
use std::net::IpAddr;
use ureq::unversioned::{
    resolver::{DefaultResolver, ResolvedSocketAddrs, Resolver},
    transport::NextTimeout,
};

pub fn loopback_host(host: &str) -> bool {
    host.eq_ignore_ascii_case("localhost")
        || host
            .trim_matches(['[', ']'])
            .parse::<IpAddr>()
            .is_ok_and(loopback)
}
fn loopback(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V6(v6) => v6
            .to_ipv4_mapped()
            .map_or_else(|| v6.is_loopback(), |v4| v4.is_loopback()),
        _ => ip.is_loopback(),
    }
}
#[derive(Debug)]
pub struct AllowedResolver {
    pub allow_explicit_loopback: bool,
}
impl Resolver for AllowedResolver {
    fn resolve(
        &self,
        uri: &ureq::http::Uri,
        config: &ureq::config::Config,
        timeout: NextTimeout,
    ) -> Result<ResolvedSocketAddrs, ureq::Error> {
        let addresses = DefaultResolver::default().resolve(uri, config, timeout)?;
        let local = self.allow_explicit_loopback && uri.host().is_some_and(loopback_host);
        if addresses.is_empty()
            || addresses.iter().any(|address| {
                if local {
                    !loopback(address.ip())
                } else {
                    !public_address(address.ip())
                }
            })
        {
            return Err(std::io::Error::new(
                std::io::ErrorKind::PermissionDenied,
                "拒绝连接未经允许的网络地址。",
            )
            .into());
        }
        Ok(addresses)
    }
}
