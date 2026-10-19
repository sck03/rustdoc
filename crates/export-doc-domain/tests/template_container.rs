use export_doc_domain::{
    designer::Design,
    report_template_format::{decode, encode},
};
use sha2::{Digest, Sha256};

#[test]
fn template_container_rejects_forged_expanded_lengths() {
    let design = Design::invoice();
    let original = encode(&design).unwrap();
    assert_eq!(decode(&original).unwrap(), design);
    // The SHA-256 is public integrity metadata, not an excuse to trust ZIP sizes.
    for declared in [0u32, 1, 4 * 1024 * 1024 + 1] {
        let mut bytes = original.clone();
        let central = bytes.windows(4).position(|s| s == b"PK\x01\x02").unwrap();
        bytes[central + 24..central + 28].copy_from_slice(&declared.to_le_bytes());
        let digest = Sha256::digest(&bytes[49..]);
        bytes[17..49].copy_from_slice(&digest);
        assert!(decode(&bytes).is_err(), "forged ZIP length {declared}");
    }
}
