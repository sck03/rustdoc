use export_doc_domain::single_window::Business;
use export_doc_single_window::{authentication, digest, receipt, xml};
use serde_json::json;

#[test]
fn receipt_xml_decodes_entities_and_rejects_ambiguous_or_expanding_input() {
    let (receipt,_)=receipt::parse(Business::Acd,b"<ImportAgrResponse><ResponseCode>0</ResponseCode><ConsignNo>A1</ConsignNo><ResponseMessage>A&amp;B &#x4E2D;<![CDATA[<ok>]]></ResponseMessage></ImportAgrResponse>","receipt.xml").unwrap();
    assert_eq!(receipt["receiptMessage"], "A&B 中<ok>");
    assert_eq!(receipt["businessStatus"], "Accepted");
    for xml in [
        "<!DOCTYPE Receipt [<!ENTITY x SYSTEM 'file:///secret'>]><Receipt>&x;</Receipt>",
        "<Receipt><CertNo>A</CertNo><CertNo>B</CertNo></Receipt>",
        "<Receipt>&unknown;</Receipt>",
        "<Receipt/>&#0;",
        "<Receipt/><Receipt/>",
        "<Receipt a='1' a='2'/>",
        "<Receipt><Code></Receipt>",
    ] {
        assert!(
            receipt::parse(Business::Coo, xml.as_bytes(), "receipt.xml").is_err(),
            "{xml}"
        );
    }
    let fields = format!(
        "<Receipt><Note>{}</Note></Receipt>",
        "x".repeat(64 * 1024 + 1)
    );
    assert!(receipt::parse(Business::Coo, fields.as_bytes(), "large.xml").is_err());
    let payload = format!(
        "<File><FileContent>{}</FileContent></File>",
        "A".repeat(1024 * 1024)
    );
    xml::validate(payload.as_bytes()).unwrap();
    let payload = format!(
        "<Certificate><CertificateList>{}</CertificateList></Certificate>",
        "<Goods><GoodsName>商品</GoodsName></Goods>".repeat(5000)
    );
    xml::validate(payload.as_bytes()).unwrap();
}

#[test]
fn assignment_and_manifest_mac_bind_company_card_and_utf16_text() {
    let secret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    let assignment = json!({"version":1,"stationKey":format!("SWS-{}","A".repeat(32)),"profileKey":format!("SWP-{}","B".repeat(32)),"profileName":"测试🧾","companyScope":"DEFAULT","cardIdentifier":"CARD","authenticationSecret":secret,"canSubmitCustomsCoo":true,"canSubmitAgentConsignment":false});
    let code = authentication::encode_assignment(&assignment).unwrap();
    assert_eq!(authentication::assignment(&code).unwrap(), assignment);
    assert!(authentication::assignment(&(code + "!")).is_err());
    let mut manifest = json!({"schemaVersion":"4.0","packageId":"id","packageType":"SubmitPackage","businessType":"CustomsCoo","batchReference":"batch","contentDigest":digest(b"payload"),"sourcePackageDigest":"","stationKey":assignment["stationKey"],"clientProfileKey":assignment["profileKey"],"cardIdentifier":"CARD","companyScope":"DEFAULT","assignmentNonce":"nonce","authenticationAlgorithm":"HMAC-SHA256"});
    manifest["authenticationTag"] = json!(authentication::sign(&manifest, secret).unwrap());
    authentication::verify(&manifest, secret).unwrap();
    manifest["companyScope"] = json!("other");
    assert!(authentication::verify(&manifest, secret).is_err());
}
