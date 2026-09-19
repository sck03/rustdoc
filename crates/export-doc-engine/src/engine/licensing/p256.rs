//! ECDSA P-256 (secp256r1) verification for the EDM2- license key format.
//!
//! The payload is the exact JSON {"v":2,"mid":<machine id>,"exp":<unix seconds>}
//! and the signature is DER-encoded ECDSA P-256 with SHA-256 over the raw payload
//! bytes. Only verification is implemented here; the program never signs keys.
//! Field arithmetic is a minimal 4xu64 limb implementation; the group order check
//! and curve membership test keep malformed public keys and signatures out.
use super::SignatureVerifier;
use super::{LIFETIME, SIGNED_PREFIX, normalize_license_key, normalize_machine_id, url_decode};
use base64::{Engine, prelude::BASE64_STANDARD};
use serde::Deserialize;
use sha2::{Digest, Sha256};

pub struct EcdsaVerifier {
    point: [u8; 65],
}
impl EcdsaVerifier {
    pub fn new(spki_base64: &str) -> std::result::Result<Self, String> {
        let der = BASE64_STANDARD
            .decode(spki_base64.trim())
            .map_err(|_| "授权公钥不是有效的 Base64 编码。".to_string())?;
        Ok(Self {
            point: spki_point(&der).ok_or_else(|| "授权公钥不是有效的 P-256 SPKI。".to_string())?,
        })
    }
}
impl SignatureVerifier for EcdsaVerifier {
    fn validate(
        &self,
        machine_id: &str,
        license_key: &str,
    ) -> std::result::Result<Option<i64>, String> {
        let machine = normalize_machine_id(machine_id);
        let key = normalize_license_key(license_key);
        if machine.is_empty() {
            return Err("机器码缺失。".into());
        }
        let signed = key
            .strip_prefix(SIGNED_PREFIX)
            .ok_or_else(|| "注册码格式无效。".to_string())?;
        let (payload_base64, signature_base64) = signed
            .split_once('.')
            .ok_or_else(|| "注册码缺少签名段。".to_string())?;
        if payload_base64.is_empty() || signature_base64.is_empty() {
            return Err("注册码载荷或签名缺失。".into());
        }
        let payload = url_decode(payload_base64)?;
        let signature = url_decode(signature_base64)?;
        let signed: SignedPayload = serde_json::from_slice(&payload)
            .map_err(|_| "注册码载荷不是有效 JSON。".to_string())?;
        if signed.v != 2 || normalize_machine_id(&signed.mid) != machine {
            return Err("注册码与当前机器码不匹配。".into());
        }
        let (r, s) = der_signature(&signature).ok_or_else(|| "注册码签名格式无效。".to_string())?;
        if !ecdsa_verify(&self.point, &payload, r, s) {
            return Err("注册码签名验证失败。".into());
        }
        Ok(if signed.exp == LIFETIME {
            None
        } else {
            Some(signed.exp)
        })
    }
}
#[derive(Deserialize)]
struct SignedPayload {
    v: i64,
    mid: String,
    exp: i64,
}
// --- P-256 (secp256r1) verification. Signing never happens in this program. ---
pub(super) type Fe = [u64; 4];
pub(super) const P: Fe = [
    0xFFFF_FFFF_FFFF_FFFF,
    0x0000_0000_FFFF_FFFF,
    0,
    0xFFFF_FFFF_0000_0001,
];
pub(super) const N: Fe = [
    0xF3B9_CAC2_FC63_2551,
    0xBCE6_FAAD_A717_9E84,
    0xFFFF_FFFF_FFFF_FFFF,
    0xFFFF_FFFF_0000_0000,
];
pub(super) const GX: Fe = [
    0xF4A1_3945_D898_C296,
    0x7703_7D81_2DEB_33A0,
    0xF8BC_E6E5_63A4_40F2,
    0x6B17_D1F2_E12C_4247,
];
pub(super) const GY: Fe = [
    0xCBB6_4068_37BF_51F5,
    0x2BCE_3357_6B31_5ECE,
    0x8EE7_EB4A_7C0F_9E16,
    0x4FE3_42E2_FE1A_7F9B,
];
pub(super) const B: Fe = [
    0x3BCE_3C3E_27D2_604B,
    0x651D_06B0_CC53_B0F6,
    0xB3EB_BD55_7698_86BC,
    0x5AC6_35D8_AA3A_93E7,
];
pub(super) const P_MINUS_2: Fe = [
    0xFFFF_FFFF_FFFF_FFFD,
    0x0000_0000_FFFF_FFFF,
    0,
    0xFFFF_FFFF_0000_0001,
];
pub(super) const N_MINUS_2: Fe = [
    0xF3B9_CAC2_FC63_254F,
    0xBCE6_FAAD_A717_9E84,
    0xFFFF_FFFF_FFFF_FFFF,
    0xFFFF_FFFF_0000_0000,
];
pub(super) const ZERO: Fe = [0, 0, 0, 0];
pub(super) const ONE: Fe = [1, 0, 0, 0];
pub(super) const TWO: Fe = [2, 0, 0, 0];
pub(super) const THREE: Fe = [3, 0, 0, 0];
pub(super) const EIGHT: Fe = [8, 0, 0, 0];

fn bit(value: &Fe, position: usize) -> u64 {
    (value[position / 64] >> (position % 64)) & 1
}
fn is_zero(value: &Fe) -> bool {
    *value == ZERO
}
fn add_words<const L: usize>(left: &[u64; L], right: &[u64; L]) -> ([u64; L], u64) {
    let mut out = [0u64; L];
    let mut carry = 0u64;
    for index in 0..L {
        let value = left[index] as u128 + right[index] as u128 + carry as u128;
        out[index] = value as u64;
        carry = (value >> 64) as u64;
    }
    (out, carry)
}
fn sub_words<const L: usize>(left: &[u64; L], right: &[u64; L]) -> ([u64; L], u64) {
    let mut out = [0u64; L];
    let mut borrow = 0u64;
    for index in 0..L {
        let value = left[index] as i128 - right[index] as i128 - borrow as i128;
        out[index] = value as u64;
        borrow = if value < 0 { 1 } else { 0 };
    }
    (out, borrow)
}
fn cmp_words<const L: usize>(left: &[u64; L], right: &[u64; L]) -> std::cmp::Ordering {
    for index in (0..L).rev() {
        match left[index].cmp(&right[index]) {
            std::cmp::Ordering::Equal => {}
            other => return other,
        }
    }
    std::cmp::Ordering::Equal
}
fn widen(value: Fe) -> [u64; 8] {
    [value[0], value[1], value[2], value[3], 0, 0, 0, 0]
}
fn mul512(left: Fe, right: Fe) -> [u64; 8] {
    let mut out = [0u64; 8];
    for index in 0..4 {
        let mut carry = 0u64;
        for position in 0..4 {
            let value = out[index + position] as u128
                + left[index] as u128 * right[position] as u128
                + carry as u128;
            out[index + position] = value as u64;
            carry = (value >> 64) as u64;
        }
        out[index + 4] = carry;
    }
    out
}
/// Long division in base 2^16 nibbles; correct for any 512-bit dividend.
fn reduce(value: [u64; 8], modulus: Fe) -> Fe {
    let m = widen(modulus);
    let mut multiples = [[0u64; 8]; 16];
    for k in 1..16 {
        multiples[k] = add_words(&multiples[k - 1], &m).0;
    }
    let mut remainder = [0u64; 8];
    for position in (0..128).rev() {
        let mut carry = 0u64;
        for limb in remainder.iter_mut() {
            let next = (*limb << 4) | carry;
            carry = *limb >> 60;
            *limb = next;
        }
        remainder[0] |= (value[position / 16] >> ((position % 16) * 4)) & 0xF;
        for k in (1..16).rev() {
            if cmp_words(&remainder, &multiples[k]) != std::cmp::Ordering::Less {
                remainder = sub_words(&remainder, &multiples[k]).0;
            }
        }
    }
    [remainder[0], remainder[1], remainder[2], remainder[3]]
}
fn add_p(left: Fe, right: Fe) -> Fe {
    let (sum, carry) = add_words(&left, &right);
    if carry != 0 {
        // 真和为 2^256 + 截断值，落在 [2^256, 2P) 内；减 P 后回绕即得正确余数。
        sub_words(&sum, &P).0
    } else if cmp_words(&sum, &P) != std::cmp::Ordering::Less {
        sub_words(&sum, &P).0
    } else {
        sum
    }
}
fn sub_p(left: Fe, right: Fe) -> Fe {
    let (difference, borrow) = sub_words(&left, &right);
    if borrow != 0 {
        add_words(&difference, &P).0
    } else {
        difference
    }
}
fn mul_p(left: Fe, right: Fe) -> Fe {
    reduce(mul512(left, right), P)
}
fn sqr_p(value: Fe) -> Fe {
    mul_p(value, value)
}
fn mul_n(left: Fe, right: Fe) -> Fe {
    reduce(mul512(left, right), N)
}
fn pow_mod(base: Fe, exponent: &Fe, modulus: Fe) -> Fe {
    let multiply = |left: Fe, right: Fe| reduce(mul512(left, right), modulus);
    let mut result = ONE;
    let mut value = reduce(widen(base), modulus);
    for position in 0..256 {
        if bit(exponent, position) == 1 {
            result = multiply(result, value);
        }
        value = multiply(value, value);
    }
    result
}
fn inv_p(value: Fe) -> Fe {
    pow_mod(value, &P_MINUS_2, P)
}
fn inv_n(value: Fe) -> Fe {
    pow_mod(value, &N_MINUS_2, N)
}
#[derive(Clone, Copy)]
pub(super) struct Affine {
    pub(super) x: Fe,
    pub(super) y: Fe,
}
pub(super) const GENERATOR: Affine = Affine { x: GX, y: GY };
#[derive(Clone, Copy)]
pub(super) struct Jacobian {
    pub(super) x: Fe,
    pub(super) y: Fe,
    pub(super) z: Fe,
    pub(super) infinity: bool,
}
impl Jacobian {
    pub(super) const fn from_affine(point: Affine) -> Self {
        Self {
            x: point.x,
            y: point.y,
            z: ONE,
            infinity: false,
        }
    }
    pub(super) const fn infinity() -> Self {
        Self {
            x: ZERO,
            y: ONE,
            z: ZERO,
            infinity: true,
        }
    }
}
pub(super) fn jac_double(point: Jacobian) -> Jacobian {
    if point.infinity || is_zero(&point.y) {
        return Jacobian::infinity();
    }
    let z2 = sqr_p(point.z);
    let z4 = sqr_p(z2);
    let x2 = sqr_p(point.x);
    let e = mul_p(sub_p(x2, z4), THREE);
    let b = sqr_p(point.y);
    let c = sqr_p(b);
    let d = mul_p(sub_p(sub_p(sqr_p(add_p(point.x, b)), x2), c), TWO);
    let f = sqr_p(e);
    let x3 = sub_p(f, add_p(d, d));
    let y3 = sub_p(mul_p(e, sub_p(d, x3)), mul_p(c, EIGHT));
    let z3 = mul_p(add_p(point.y, point.y), point.z);
    Jacobian {
        x: x3,
        y: y3,
        z: z3,
        infinity: false,
    }
}
pub(super) fn jac_add_mixed(point: Jacobian, other: &Affine) -> Jacobian {
    if point.infinity {
        return Jacobian::from_affine(*other);
    }
    let z2 = sqr_p(point.z);
    let u = mul_p(other.x, z2);
    let s = mul_p(mul_p(other.y, point.z), z2);
    if cmp_words(&u, &point.x) == std::cmp::Ordering::Equal {
        if cmp_words(&s, &point.y) == std::cmp::Ordering::Equal {
            return jac_double(point);
        }
        return Jacobian::infinity();
    }
    let h = sub_p(u, point.x);
    let r = sub_p(s, point.y);
    let h2 = sqr_p(h);
    let h3 = mul_p(h2, h);
    let x3 = sub_p(sub_p(sqr_p(r), h3), mul_p(add_p(point.x, point.x), h2));
    let y3 = sub_p(mul_p(r, sub_p(mul_p(point.x, h2), x3)), mul_p(point.y, h3));
    let z3 = mul_p(h, point.z);
    Jacobian {
        x: x3,
        y: y3,
        z: z3,
        infinity: false,
    }
}
pub(super) fn scalar_mult(scalar: Fe, point: &Affine) -> Jacobian {
    let mut result = Jacobian::infinity();
    for position in (0..256).rev() {
        result = jac_double(result);
        if bit(&scalar, position) == 1 {
            result = jac_add_mixed(result, point);
        }
    }
    result
}
pub(super) fn jac_to_affine(point: Jacobian) -> Option<Affine> {
    if point.infinity {
        return None;
    }
    let z_inverse = inv_p(point.z);
    let z2 = sqr_p(z_inverse);
    Some(Affine {
        x: mul_p(point.x, z2),
        y: mul_p(point.y, mul_p(z_inverse, z2)),
    })
}
pub(super) fn on_curve(point: &Affine) -> bool {
    sqr_p(point.y)
        == add_p(
            sub_p(mul_p(sqr_p(point.x), point.x), mul_p(point.x, THREE)),
            B,
        )
}
fn ecdsa_verify(public_key: &[u8; 65], message: &[u8], r: Fe, s: Fe) -> bool {
    let Some(q) = affine_from_point(public_key) else {
        return false;
    };
    if !on_curve(&q)
        || is_zero(&r)
        || is_zero(&s)
        || cmp_words(&r, &N) != std::cmp::Ordering::Less
        || cmp_words(&s, &N) != std::cmp::Ordering::Less
    {
        return false;
    }
    let z = int_from_be(&Sha256::digest(message)).unwrap();
    let w = inv_n(s);
    let mixed = jac_add_mixed(
        scalar_mult(mul_n(w, z), &GENERATOR),
        &jac_to_affine(scalar_mult(mul_n(w, r), &q)).unwrap(),
    );
    let Some(sum) = jac_to_affine(mixed) else {
        return false;
    };
    let x = if cmp_words(&sum.x, &N) != std::cmp::Ordering::Less {
        sub_words(&sum.x, &N).0
    } else {
        sum.x
    };
    cmp_words(&x, &r) == std::cmp::Ordering::Equal
}
fn affine_from_point(point: &[u8; 65]) -> Option<Affine> {
    if point[0] != 4 {
        return None;
    }
    let x = int_from_be(&point[1..33])?;
    let y = int_from_be(&point[33..65])?;
    if cmp_words(&x, &P) != std::cmp::Ordering::Less
        || cmp_words(&y, &P) != std::cmp::Ordering::Less
    {
        return None;
    }
    Some(Affine { x, y })
}

// --- DER parsing (short-form lengths only, which is all P-256 ever needs). ---
struct Element<'a> {
    tag: u8,
    content: &'a [u8],
}
fn der_element(bytes: &[u8]) -> Option<(Element<'_>, &[u8])> {
    let (tag, rest) = bytes.split_first()?;
    let (length, rest) = rest.split_first()?;
    if *length & 0x80 != 0 || *length as usize > rest.len() {
        return None;
    }
    Some((
        Element {
            tag: *tag,
            content: &rest[..*length as usize],
        },
        &rest[*length as usize..],
    ))
}
fn der_signature(bytes: &[u8]) -> Option<(Fe, Fe)> {
    let (sequence, rest) = der_element(bytes)?;
    let (first, after) = der_element(sequence.content)?;
    let (second, tail) = der_element(after)?;
    if sequence.tag != 0x30
        || first.tag != 0x02
        || second.tag != 0x02
        || !rest.is_empty()
        || !tail.is_empty()
    {
        return None;
    }
    Some((int_from_be(first.content)?, int_from_be(second.content)?))
}
fn spki_point(der: &[u8]) -> Option<[u8; 65]> {
    let (outer, rest) = der_element(der)?;
    let (algorithm, after) = der_element(outer.content)?;
    let (bits, tail) = der_element(after)?;
    if outer.tag != 0x30
        || algorithm.tag != 0x30
        || bits.tag != 0x03
        || !rest.is_empty()
        || !tail.is_empty()
    {
        return None;
    }
    let content = bits.content;
    if content.len() != 66 || content[0] != 0 || content[1] != 4 {
        return None;
    }
    let mut point = [0u8; 65];
    point.copy_from_slice(&content[1..]);
    Some(point)
}
fn int_from_be(bytes: &[u8]) -> Option<Fe> {
    // DER 整数可带一个前导零字节以保持非负性，P-256 的 r/s 经常是 33 字节。
    let bytes = bytes.strip_prefix(&[0]).unwrap_or(bytes);
    if bytes.len() > 32 {
        return None;
    }
    let mut padded = [0u8; 32];
    padded[32 - bytes.len()..].copy_from_slice(bytes);
    let mut value = [0u64; 4];
    for (index, chunk) in padded.chunks_exact(8).enumerate() {
        value[3 - index] = u64::from_be_bytes(chunk.try_into().ok()?);
    }
    Some(value)
}
