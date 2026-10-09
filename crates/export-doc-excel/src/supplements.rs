use crate::{Check, Result, import::at};
use calamine::{Data, Range};
use export_doc_contracts::generated_api::ApiExcelImportAnalysisIssueDto;
use export_doc_domain::invoice_columns::{ITEM_COLUMNS, ItemRow};
use exportdoc_excel_analyzer::{FieldCandidate, item_metadata_fields};
use std::collections::{BTreeMap, BTreeSet};

fn key(value: &str) -> String {
    value.trim().to_ascii_uppercase()
}

fn identifier_column(
    rows: &[Vec<String>],
    fields: &[FieldCandidate],
    name: &str,
    known: &BTreeSet<String>,
) -> Option<usize> {
    if let Some(field) = fields.iter().find(|field| field.canonical_field == name) {
        return Some(field.column - 1);
    }
    // Unlabelled identifiers are accepted only when one column matches the
    // imported business keys. Never join by row order or by a fixed column.
    let mut counts = BTreeMap::<usize, usize>::new();
    for row in rows {
        for (col, value) in row.iter().enumerate() {
            if !fields.iter().any(|field| field.column == col + 1) && known.contains(&key(value)) {
                *counts.entry(col).or_default() += 1;
            }
        }
    }
    let max = counts.values().copied().max()?;
    let mut best = counts.into_iter().filter(|(_, count)| *count == max);
    let (col, _) = best.next()?;
    best.next().is_none().then_some(col)
}

fn warning(row: usize, message: &str) -> ApiExcelImportAnalysisIssueDto {
    ApiExcelImportAnalysisIssueDto {
        severity: "Warning".into(),
        code: "ItemSupplementConflict".into(),
        message: format!("商品补充表第 {row} 行：{message}"),
        ..Default::default()
    }
}

pub(crate) struct Tables {
    offset: usize,
    cells: Vec<Vec<String>>,
    headers: Vec<(usize, Vec<FieldCandidate>)>,
}

impl Tables {
    pub(crate) fn read(range: &Range<Data>, after_row: usize, check: Check<'_>) -> Result<Self> {
        let Some(end) = range.end() else {
            return Ok(Self {
                offset: 0,
                cells: vec![],
                headers: vec![],
            });
        };
        let mut cells = Vec::new();
        let mut headers = Vec::new();
        let mut offset = 0;
        for row in after_row + 1..=end.0 as usize + 1 {
            check()?;
            let values = (1..=end.1 as usize + 1)
                .map(|col| at(range, row, col))
                .collect();
            let fields = item_metadata_fields(std::slice::from_ref(&values), 0);
            if !fields.is_empty() {
                if headers.is_empty() {
                    offset = row - 1;
                }
                headers.push((cells.len(), fields));
            }
            // Keep only the supplement region; ordinary imports do not need a
            // second in-memory copy of all product cells.
            if !headers.is_empty() {
                cells.push(values);
            }
        }
        Ok(Self {
            offset,
            cells,
            headers,
        })
    }

    pub(crate) fn detail_end(&self) -> Option<usize> {
        self.headers.first().map(|(row, _)| self.offset + row)
    }

    pub(crate) fn apply(
        self,
        items: &mut [ItemRow],
        source_fields: &[Vec<usize>],
        check: Check<'_>,
    ) -> Result<Vec<ApiExcelImportAnalysisIssueDto>> {
        let Self {
            offset: sheet_offset,
            cells,
            headers,
        } = self;
        let mut by_style = BTreeMap::<String, BTreeMap<String, Vec<usize>>>::new();
        let mut known_pos = BTreeSet::new();
        for (index, item) in items.iter().enumerate() {
            let style = key(&item.cells[1]);
            let po = key(&item.cells[0]);
            if style.is_empty() {
                continue;
            }
            if !po.is_empty() {
                known_pos.insert(po.clone());
            }
            by_style
                .entry(style)
                .or_default()
                .entry(po)
                .or_default()
                .push(index);
        }
        let known_styles = by_style.keys().cloned().collect();
        let mut patches = vec![BTreeMap::<usize, Option<String>>::new(); items.len()];
        let mut issues = vec![];
        for (table_index, (header, fields)) in headers.iter().enumerate() {
            let end = headers
                .get(table_index + 1)
                .map_or(cells.len(), |(row, _)| *row);
            let rows = &cells[header + 1..end];
            let Some(style_col) = identifier_column(rows, fields, "StyleNo", &known_styles) else {
                continue;
            };
            let po_col = identifier_column(rows, fields, "PoNumber", &known_pos);
            let mut ambiguous = BTreeSet::new();
            for (offset, row) in rows.iter().enumerate() {
                check()?;
                let value = |col| row.get(col).map(String::as_str).unwrap_or("");
                let style = key(value(style_col));
                let Some(pos) = by_style.get(&style) else {
                    continue;
                };
                let source_row = sheet_offset + header + offset + 2;
                let matches = if let Some(col) = po_col {
                    pos.get(&key(value(col)))
                } else if pos.len() == 1 {
                    pos.values().next()
                } else {
                    if ambiguous.insert(style) {
                        issues.push(warning(
                            source_row,
                            "款号对应多个 PO，无法确定归属，未合并。",
                        ));
                    }
                    None
                };
                let Some(matches) = matches else {
                    continue;
                };
                for field in fields {
                    let raw = value(field.column - 1).trim();
                    if raw.is_empty()
                        || matches!(field.canonical_field.as_str(), "StyleNo" | "PoNumber")
                    {
                        continue;
                    }
                    if matches!(
                        raw,
                        "#REF!" | "#VALUE!" | "#N/A" | "#DIV/0!" | "#NAME?" | "#NUM!" | "#NULL!"
                    ) {
                        issues.push(warning(
                            source_row,
                            "补充字段包含 Excel 公式错误，未合并该值。",
                        ));
                        continue;
                    }
                    let name = if field.canonical_field == "UnitEN"
                        && raw.chars().any(|c| ('\u{4e00}'..='\u{9fff}').contains(&c))
                    {
                        "UnitCN"
                    } else {
                        &field.canonical_field
                    };
                    let Some(index) = ITEM_COLUMNS
                        .iter()
                        .position(|column| column.key.eq_ignore_ascii_case(name))
                    else {
                        continue;
                    };
                    for &item in matches {
                        if source_fields[item].contains(&index) {
                            continue;
                        }
                        match patches[item].entry(index) {
                            std::collections::btree_map::Entry::Vacant(entry) => {
                                entry.insert(Some(raw.into()));
                            }
                            std::collections::btree_map::Entry::Occupied(mut entry) => {
                                if entry.get().as_deref().is_some_and(|old| old != raw) {
                                    entry.insert(None);
                                    issues.push(warning(
                                        source_row,
                                        "同一 PO/款号的补充信息相互冲突，未合并该字段。",
                                    ));
                                }
                            }
                        }
                    }
                }
            }
        }
        for (item, fields) in items.iter_mut().zip(patches) {
            for (index, value) in fields {
                if let Some(value) = value {
                    item.cells[index] = value;
                }
            }
        }
        Ok(issues)
    }
}
