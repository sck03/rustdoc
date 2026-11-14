use super::*;

#[test]
fn addressing_preserves_bucket_hosts_and_encodes_prefixes_once() {
    for (addressing, host, path) in [
        (
            S3Addressing::Path,
            "objects.example.com",
            "/bucket-name/a%20b/%E8%B5%84%E6%96%99/repository/file.txt",
        ),
        (
            S3Addressing::VirtualHost,
            "bucket-name.objects.example.com",
            "/a%20b/%E8%B5%84%E6%96%99/repository/file.txt",
        ),
        (
            S3Addressing::BucketHost,
            "objects.example.com",
            "/a%20b/%E8%B5%84%E6%96%99/repository/file.txt",
        ),
    ] {
        let settings = S3Settings {
            endpoint: "https://objects.example.com".into(),
            bucket: "bucket-name".into(),
            access_key_id: "access".into(),
            prefix: "a b/资料".into(),
            addressing,
            ..Default::default()
        };
        let target = Target::new(settings, "repository", "secret", "token").unwrap();
        let url = target.url("file.txt").unwrap();
        assert_eq!(url.host_str(), Some(host));
        assert_eq!(url.path(), path);
        let request = target.request("GET", &url, (), None, EMPTY_HASH).unwrap();
        assert_eq!(request.headers()["x-amz-security-token"], "token");
    }
}
#[test]
fn incompatible_endpoint_modes_are_rejected_before_network_io() {
    for endpoint in [
        "https://oss-cn-hangzhou.aliyuncs.com",
        "https://cos.ap-guangzhou.myqcloud.com",
        "https://s3.example.com/bucket",
    ] {
        let settings = S3Settings {
            endpoint: endpoint.into(),
            bucket: "bucket-name".into(),
            access_key_id: "access".into(),
            addressing: S3Addressing::Path,
            ..Default::default()
        };
        assert!(validate(&settings).is_err());
    }
}
