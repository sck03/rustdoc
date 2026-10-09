use super::{build_field_candidates, FieldCandidate};
use std::collections::BTreeMap;

/// Recognize descriptive tables without treating them as quantity/price rows.
/// Uses the same field vocabulary as the primary item-table analyzer.
pub fn item_metadata_fields(cells: &[Vec<String>], row: usize) -> Vec<FieldCandidate> {
    let fields = build_field_candidates(cells, row, 1);
    if fields.iter().any(|field| {
        matches!(
            field.canonical_field.as_str(),
            "Quantity" | "UnitPrice" | "TotalPrice" | "Cartons"
        )
    }) {
        return vec![];
    }
    let mut unique: BTreeMap<String, FieldCandidate> = BTreeMap::new();
    for field in fields.into_iter().filter(|field| {
        matches!(
            field.canonical_field.as_str(),
            "PoNumber"
                | "StyleNo"
                | "StyleNameCN"
                | "FabricComposition"
                | "Brand"
                | "HSCode"
                | "Origin"
                | "UnitEN"
                | "UnitCN"
        )
    }) {
        let entry = unique
            .entry(field.canonical_field.clone())
            .or_insert_with(|| field.clone());
        if field.confidence > entry.confidence {
            *entry = field;
        }
    }
    if unique
        .keys()
        .filter(|key| !matches!(key.as_str(), "PoNumber" | "StyleNo"))
        .count()
        < 2
    {
        return vec![];
    }
    unique.into_values().collect()
}
