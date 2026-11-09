use export_doc_domain::single_window::Business;
use export_doc_single_window::{authentication, digest, receipt, xml};
use serde_json::json;
#[path = "support/declarations.rs"]
mod declarations;

#[test]
fn common_certificates_validate_and_preserve_independent_parties_and_goods() {
    use export_doc_domain::single_window::{draft, validation, xml as output};
    for (cert, country, sequence) in declarations::COMMON {
        let document = declarations::coo(cert, country, sequence);
        assert!(
            validation::review(Business::Coo, &document).is_empty(),
            "{cert}"
        );
        let text = String::from_utf8(output::payload(Business::Coo, &document).unwrap()).unwrap();
        assert!(text.contains("DESTINATION RECEIVER/nTEST ADDRESS"));
        assert!(text.contains("100% COTTON KNITTED T-SHIRTS, ADULT"));
        if cert == "RC" {
            assert!(text.contains("<OriInvs><OriInv>"));
        }
        let mut missing = document.clone();
        missing["items"] = json!([]);
        assert!(!validation::review(Business::Coo, &missing).is_empty());
        let mut saved = document.clone();
        saved["contractNo"] = json!("DESTINATION-CONTRACT");
        saved["items"][0]["goodsNameE"] = json!("DETAILED NAME");
        saved["items"][0]["sourceItemId"] = json!(42);
        let locks = draft::locks(Business::Coo, &saved, &document);
        let mut reloaded = document.clone();
        draft::restore_locked(Business::Coo, &mut reloaded, &saved, &locks);
        assert_eq!(reloaded["items"], saved["items"]);
        assert_eq!(reloaded["contractNo"], "DESTINATION-CONTRACT");
    }
    let acd = declarations::acd();
    assert!(export_doc_domain::single_window::validation::review(Business::Acd, &acd).is_empty());
    for field in [
        "copCusCode",
        "gName",
        "codeTS",
        "declTotal",
        "ieDate",
        "tradeMode",
        "oriCountry",
        "tradeCode",
        "agentCode",
    ] {
        let mut missing = acd.clone();
        missing[field] = json!("");
        assert!(
            validation::review(Business::Acd, &missing)
                .iter()
                .any(|issue| issue.field == field),
            "{field}"
        );
    }
    for (cert, country, sequence) in declarations::COMMON {
        let mut missing = declarations::coo(cert, country, sequence);
        missing["items"][0]["oriCriteria"] = json!("");
        assert_eq!(
            validation::review(Business::Coo, &missing)
                .iter()
                .any(|issue| issue.field == "oriCriteria"),
            cert != "C"
        );
    }
    let mut rcep = declarations::coo("RC", "JAPAN", "9005");
    rcep["invNo"] = json!("");
    assert!(
        validation::review(Business::Coo, &rcep)
            .iter()
            .any(|issue| issue.field == "invNo")
    );
    let mut wrong_date = acd.clone();
    wrong_date["ieDate"] = json!("2026-11-09");
    assert!(
        validation::review(Business::Acd, &wrong_date)
            .iter()
            .any(|issue| issue.field == "ieDate")
    );
    for (field, value) in [
        ("gName", "货".repeat(256)),
        ("tradeMode", "x".into()),
        ("declTotal", "1234567890123456.1234".into()),
    ] {
        let mut invalid = acd.clone();
        invalid[field] = json!(value);
        assert!(
            validation::review(Business::Acd, &invalid)
                .iter()
                .any(|issue| issue.field == field),
            "{field}"
        );
    }
}

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
    let mut manifest = json!({"schemaVersion":"5.0","packageId":"id","packageType":"SubmitPackage","businessType":"CustomsCoo","batchReference":"batch","contentDigest":digest(b"payload"),"sourcePackageDigest":"","stationKey":assignment["stationKey"],"clientProfileKey":assignment["profileKey"],"cardIdentifier":"CARD","companyScope":"DEFAULT","assignmentNonce":"nonce","authenticationAlgorithm":"HMAC-SHA256"});
    manifest["authenticationTag"] = json!(authentication::sign(&manifest, secret).unwrap());
    authentication::verify(&manifest, secret).unwrap();
    manifest["companyScope"] = json!("other");
    assert!(authentication::verify(&manifest, secret).is_err());
}
