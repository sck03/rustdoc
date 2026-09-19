use std::net::IpAddr;
pub fn public_address(address: IpAddr) -> bool {
    match address {
        IpAddr::V4(ip) => {
            let [a, b, c, _] = ip.octets();
            !(a == 0
                || a == 10
                || a == 127
                || a >= 224
                || (a == 100 && (64..=127).contains(&b))
                || (a == 169 && b == 254)
                || (a == 172 && (16..=31).contains(&b))
                || (a == 192
                    && ((b == 0 && (c == 0 || c == 2)) || (b == 88 && c == 99) || b == 168))
                || (a == 198 && ((18..=19).contains(&b) || (b == 51 && c == 100)))
                || (a == 203 && b == 0 && c == 113))
        }
        IpAddr::V6(ip) => {
            if let Some(mapped) = ip.to_ipv4_mapped() {
                return public_address(IpAddr::V4(mapped));
            }
            let b = ip.octets();
            (b[0] & 0xe0) == 0x20
                && !(b[0] == 0x20
                    && b[1] == 0x01
                    && ((b[2] == 0x0d && b[3] == 0xb8)
                        || (b[2] == 0 && b[3] == 2)
                        || (b[2] == 0 && matches!(b[3] & 0xf0, 0x10 | 0x20))))
                && !(b[0] == 0x20 && b[1] == 2)
        }
    }
}
