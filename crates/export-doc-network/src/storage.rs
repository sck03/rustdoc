//! Administrator-selected storage may live on a NAS. Pin DNS answers, retain TLS
//! validation, and bound each socket read/write even when the whole file takes hours.
use super::{client_config, loopback_host, public_address};
use std::{
    net::IpAddr,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::Duration,
};
use ureq::unversioned::{
    resolver::{DefaultResolver, ResolvedSocketAddrs, Resolver},
    transport::{Buffers, ConnectionDetails, Connector, DefaultConnector, NextTimeout, Transport},
};

fn permitted(ip: IpAddr, explicit_loopback: bool) -> bool {
    match ip {
        IpAddr::V4(ip) => {
            public_address(ip.into()) || ip.is_private() || (explicit_loopback && ip.is_loopback())
        }
        IpAddr::V6(ip) => ip.to_ipv4_mapped().map_or_else(
            || {
                public_address(ip.into())
                    || ip.is_unique_local()
                    || (explicit_loopback && ip.is_loopback())
            },
            |ip| permitted(ip.into(), explicit_loopback),
        ),
    }
}

#[derive(Debug)]
struct StorageResolver;
impl Resolver for StorageResolver {
    fn resolve(
        &self,
        uri: &ureq::http::Uri,
        config: &ureq::config::Config,
        timeout: NextTimeout,
    ) -> Result<ResolvedSocketAddrs, ureq::Error> {
        let addresses = DefaultResolver::default().resolve(uri, config, timeout)?;
        let local = uri.host().is_some_and(loopback_host);
        if addresses.is_empty()
            || addresses
                .iter()
                .any(|address| !permitted(address.ip(), local))
        {
            return Err(std::io::Error::new(
                std::io::ErrorKind::PermissionDenied,
                "备份存储地址不允许连接。",
            )
            .into());
        }
        Ok(addresses)
    }
}

#[derive(Debug)]
struct StorageConnector(Arc<AtomicBool>);
impl Connector for StorageConnector {
    type Out = StorageTransport;
    fn connect(
        &self,
        details: &ConnectionDetails,
        chained: Option<()>,
    ) -> Result<Option<Self::Out>, ureq::Error> {
        check(&self.0)?;
        Ok(DefaultConnector::new()
            .connect(details, chained)?
            .map(|inner| StorageTransport {
                inner,
                canceled: self.0.clone(),
            }))
    }
}
#[derive(Debug)]
struct StorageTransport {
    inner: Box<dyn Transport>,
    canceled: Arc<AtomicBool>,
}
fn check(canceled: &AtomicBool) -> Result<(), ureq::Error> {
    if canceled.load(Ordering::Acquire) {
        Err(std::io::Error::new(std::io::ErrorKind::Interrupted, "存储传输已取消。").into())
    } else {
        Ok(())
    }
}
fn bounded(mut timeout: NextTimeout) -> NextTimeout {
    timeout.after = timeout.after.min(Duration::from_secs(30).into());
    timeout
}
impl Transport for StorageTransport {
    fn buffers(&mut self) -> &mut dyn Buffers {
        self.inner.buffers()
    }
    fn transmit_output(&mut self, amount: usize, timeout: NextTimeout) -> Result<(), ureq::Error> {
        check(&self.canceled)?;
        self.inner.transmit_output(amount, bounded(timeout))
    }
    fn await_input(&mut self, timeout: NextTimeout) -> Result<bool, ureq::Error> {
        check(&self.canceled)?;
        self.inner.await_input(bounded(timeout))
    }
    fn is_open(&mut self) -> bool {
        !self.canceled.load(Ordering::Acquire) && self.inner.is_open()
    }
    fn is_tls(&self) -> bool {
        self.inner.is_tls()
    }
}

pub fn storage_agent(timeout: Duration, canceled: Arc<AtomicBool>) -> ureq::Agent {
    ureq::Agent::with_parts(
        client_config(timeout, true),
        StorageConnector(canceled),
        StorageResolver,
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn nas_addresses_are_allowed_without_opening_metadata_or_public_feed_policy() {
        for address in [
            "10.0.0.2",
            "172.16.1.2",
            "192.168.1.2",
            "fd12::2",
            "::ffff:192.168.1.2",
        ] {
            let ip = address.parse().unwrap();
            assert!(permitted(ip, false));
            assert!(!public_address(ip));
        }
        for address in [
            "0.0.0.0",
            "169.254.169.254",
            "224.0.0.1",
            "::",
            "fe80::1",
            "ff02::1",
            "127.0.0.1",
            "::1",
        ] {
            assert!(!permitted(address.parse().unwrap(), false));
        }
        assert!(permitted("127.0.0.1".parse().unwrap(), true));
    }
}
