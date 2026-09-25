//! Official contract composition. The frozen .NET export is preserved verbatim;
//! new Rust capabilities are added here and both clients consume the same output.
mod office;
use serde_json::Value;

pub fn document() -> Value {
    let mut document: Value = serde_json::from_str(include_str!("reference_openapi.json"))
        .expect("reviewed reference OpenAPI");
    office::extend(&mut document);
    document
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn generated_document_matches_the_rust_composer() {
        let published: Value = serde_json::from_str(include_str!("openapi.json")).unwrap();
        assert!(
            published == document(),
            "Regenerate both clients from the Rust OpenAPI composer."
        );
    }

    #[test]
    fn preserves_every_reference_schema_endpoint_and_security_contract() {
        let baseline: Value = serde_json::from_str(include_str!("reference_openapi.json")).unwrap();
        let mut current = document();
        assert_eq!(
            current["components"]["schemas"]["PersonnelClearance"]["properties"]["approvalCount"]["type"],
            "integer"
        );
        current["components"]["schemas"]["PersonnelClearance"]["properties"]
            .as_object_mut()
            .unwrap()
            .remove("approvalCount");
        for (path, methods) in baseline["paths"].as_object().unwrap() {
            assert_eq!(&current["paths"][path], methods, "endpoint drift: {path}");
        }
        for (name, schema) in baseline["components"]["schemas"].as_object().unwrap() {
            assert_eq!(
                &current["components"]["schemas"][name], schema,
                "schema drift: {name}"
            );
        }
        assert_eq!(
            current["components"]["securitySchemes"],
            baseline["components"]["securitySchemes"]
        );
        assert_eq!(
            current["x-exportdoc-configuration"],
            baseline["x-exportdoc-configuration"]
        );
        for operation in current["paths"]
            .as_object()
            .unwrap()
            .values()
            .flat_map(|v| v.as_object().unwrap().values())
        {
            assert!(operation.get("x-exportdoc-policy").is_some());
        }
    }
}
