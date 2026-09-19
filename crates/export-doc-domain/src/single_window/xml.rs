use super::{Business, reference, text};
use serde_json::Value;
fn escape(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}
pub fn multiline(value: &str) -> String {
    value
        .replace("/n", "\n")
        .replace("/N", "\n")
        .replace("\r\n", "\n")
        .replace('\r', "\n")
        .lines()
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .collect::<Vec<_>>()
        .join("/n")
}
fn element(out: &mut String, tag: &str, value: &str) {
    out.push_str(&format!("<{tag}>{}</{tag}>", escape(value)));
}
fn fields(out: &mut String, scope: &str, value: &Value) {
    for field in reference()["xml"][scope].as_array().into_iter().flatten() {
        let key = text(field, "key");
        let value = value[key]
            .as_str()
            .map(str::to_owned)
            .unwrap_or_else(|| value[key].to_string());
        let value = if field["multiline"] == true {
            multiline(&value)
        } else {
            value
        };
        element(out, text(field, "tag"), &value);
    }
}
pub fn payload(business: Business, document: &Value) -> Result<Vec<u8>, String> {
    super::validation::structure(business, document)?;
    let mut out = String::from("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    if business == Business::Acd {
        out.push_str("<ImportAgrRequest xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"AcdAgrInfo_ImportSave.xsd\"><OperInfo>");
        for (i, field) in reference()["xml"]["acd"]
            .as_array()
            .into_iter()
            .flatten()
            .enumerate()
        {
            if i == 3 {
                out.push_str("</OperInfo><ImportInfo>");
            }
            element(
                &mut out,
                text(field, "tag"),
                text(document, text(field, "key")),
            );
        }
        out.push_str("</ImportInfo></ImportAgrRequest>");
        return Ok(out.into_bytes());
    }
    out.push_str("<Certificate xmlns=\"http://www.w3.org/2000/09/xmldsig#\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"http://www.w3.org/2000/09/xmldsig# coo.xsd\"><CertificateHead>");
    fields(&mut out, "coo", document);
    out.push_str("</CertificateHead><CertificateList>");
    for item in document["items"].as_array().into_iter().flatten() {
        out.push_str("<Goods>");
        fields(&mut out, "goods", item);
        out.push_str("</Goods>");
    }
    out.push_str("</CertificateList>");
    let modifications = [
        ("OldCertNo", "oldCertNo"),
        ("ModReason", "modReason"),
        ("ModColm", "modColm"),
        ("OldSituDesc", "oldSituDesc"),
        ("ModSituDesc", "modSituDesc"),
        ("OldDeclDate", "oldDeclDate"),
        ("OldIssueDate", "oldIssueDate"),
    ];
    if ["1", "2", "3"].contains(&text(document, "certStatus"))
        || modifications
            .iter()
            .any(|(_, key)| !text(document, key).is_empty())
    {
        out.push_str("<ModCertificate>");
        for (tag, key) in modifications {
            element(&mut out, tag, &multiline(text(document, key)));
        }
        out.push_str("</ModCertificate>");
    }
    if let Some(rows) = document["nonpartyCorps"]
        .as_array()
        .filter(|r| !r.is_empty())
    {
        out.push_str("<NonpartyCorpList>");
        for (i, row) in rows.iter().enumerate() {
            out.push_str("<NonpartyCorp>");
            element(&mut out, "SortNo", &(i + 1).to_string());
            for (tag, key) in [
                ("EntName", "entName"),
                ("EntAddr", "entAddr"),
                ("EntCountryCode", "entCountryCode"),
                ("EntCountryName", "entCountryName"),
            ] {
                element(&mut out, tag, &multiline(text(row, key)));
            }
            out.push_str("</NonpartyCorp>");
        }
        out.push_str("</NonpartyCorpList>");
    }
    if text(document, "certType") == "RC" {
        out.push_str("<OriInvs><OriInv>");
        for (tag, key, required) in [
            ("CertNo", "certNo", true),
            ("InvNo", "invNo", true),
            ("ContractNo", "contractNo", false),
            ("LcNo", "lcNo", false),
            ("Value", "totalAmt", false),
            ("Curr", "curr", true),
            ("PriceClause", "priceTerms", true),
            ("SpecInvTerms", "specInvTerms", false),
            ("InvDate", "invDate", true),
        ] {
            if required || !text(document, key).is_empty() {
                element(&mut out, tag, &multiline(text(document, key)));
            }
        }
        out.push_str("</OriInv></OriInvs>");
    }
    out.push_str("<AplPromise>");
    element(&mut out, "AplPromiseCode", text(document, "aplPromiseCode"));
    out.push_str("</AplPromise></Certificate>");
    Ok(out.into_bytes())
}
