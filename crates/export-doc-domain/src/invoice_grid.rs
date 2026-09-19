//! Spreadsheet gestures have no GUI dependencies; each successful gesture is atomic.
use crate::{
    history::History,
    invoice::{InvoiceDraft, ItemRow, MAX_ROWS, parse_tsv},
    invoice_columns::{COLUMN_COUNT, ITEM_COLUMNS},
};

#[derive(Clone, Copy, Default, Debug, PartialEq, Eq)]
pub struct Cell {
    pub row: usize,
    pub column: usize,
}
#[derive(Clone, Copy, Default, Debug, PartialEq, Eq)]
pub struct Selection {
    pub anchor: Cell,
    pub active: Cell,
}
impl Selection {
    pub fn contains(&self, row: usize, column: usize) -> bool {
        (self.anchor.row.min(self.active.row)..=self.anchor.row.max(self.active.row)).contains(&row)
            && (self.anchor.column.min(self.active.column)
                ..=self.anchor.column.max(self.active.column))
                .contains(&column)
    }
}
pub struct InvoiceGrid {
    pub draft: InvoiceDraft,
    pub selection: Selection,
    pub visible: Vec<usize>,
    pub history: History<InvoiceDraft>,
    baseline: InvoiceDraft,
}
impl InvoiceGrid {
    pub fn new(draft: InvoiceDraft, spare_columns: usize) -> Self {
        let visible = (0..COLUMN_COUNT)
            .filter(|&i| {
                i < 28 + spare_columns.min(10)
                    || draft.rows.iter().any(|row| !row.cells[i].trim().is_empty())
            })
            .collect();
        Self {
            baseline: draft.clone(),
            draft,
            selection: Selection::default(),
            visible,
            history: History::new(60),
        }
    }
    pub fn dirty(&self) -> bool {
        self.draft != self.baseline
    }
    pub fn saved(&mut self, draft: InvoiceDraft) {
        self.baseline = draft.clone();
        self.draft = draft;
    }
    pub fn select(&mut self, row: usize, column: usize, extend: bool) {
        let cell = Cell {
            row: row.min(MAX_ROWS - 1),
            column: column.min(self.visible.len().saturating_sub(1)),
        };
        self.selection.active = cell;
        if !extend {
            self.selection.anchor = cell;
        }
        self.history.end_group();
    }
    pub fn move_selection(&mut self, rows: isize, columns: isize, extend: bool) {
        self.select(
            self.selection
                .active
                .row
                .saturating_add_signed(rows)
                .min(self.draft.rows.len().max(6).min(MAX_ROWS - 1)),
            self.selection.active.column.saturating_add_signed(columns),
            extend,
        );
    }
    pub fn set_visible(&mut self, column: usize, visible: bool) -> Result<(), String> {
        if column >= COLUMN_COUNT {
            return Err("列不存在。".into());
        }
        if visible && !self.visible.contains(&column) {
            self.visible.push(column);
            self.visible.sort_unstable();
        }
        if !visible {
            if self.visible.len() == 1 {
                return Err("至少保留一列。".into());
            }
            self.visible.retain(|&i| i != column);
        }
        self.select(
            self.selection.active.row,
            self.selection.active.column,
            false,
        );
        Ok(())
    }
    pub fn edit(&mut self, row: usize, displayed_column: usize, text: &str) -> Result<(), String> {
        self.apply(row, displayed_column, vec![vec![text.into()]])
    }
    pub fn paste(&mut self, text: &str) -> Result<(), String> {
        if text.len() > 4 * 1024 * 1024 {
            return Err("粘贴内容超过 4 MiB。".into());
        }
        self.apply(
            self.selection.active.row,
            self.selection.active.column,
            parse_tsv(text)?,
        )
    }
    fn apply(&mut self, row: usize, column: usize, rows: Vec<Vec<String>>) -> Result<(), String> {
        if rows.is_empty() {
            return Ok(());
        }
        if row.checked_add(rows.len()).is_none_or(|end| end > MAX_ROWS) {
            return Err(format!("最多 {MAX_ROWS} 行。"));
        }
        if column >= self.visible.len()
            || rows.iter().any(|r| r.len() + column > self.visible.len())
        {
            return Err("粘贴内容超过当前显示列；请先调整显示列。".into());
        }
        let mut draft = self.draft.clone();
        for (offset, cells) in rows.iter().enumerate() {
            while draft.rows.len() <= row + offset {
                draft.rows.push(ItemRow::blank());
            }
            let item = &mut draft.rows[row + offset];
            let mut changed = vec![];
            for (i, value) in cells.iter().enumerate() {
                let actual = self.visible[column + i];
                item.cells[actual] = value.clone();
                changed.push(actual);
            }
            item.recalculate(&changed)
                .map_err(|error| format!("第 {} 行：{error}", row + offset + 1))?;
            item.to_dto()
                .map_err(|error| format!("第 {} 行：{error}", row + offset + 1))?;
        }
        self.replace(draft);
        Ok(())
    }
    pub fn replace(&mut self, draft: InvoiceDraft) {
        let before = std::mem::replace(&mut self.draft, draft);
        self.history.record(before, &self.draft, None);
    }
    pub fn copy(&self) -> String {
        let a = self.selection.anchor;
        let b = self.selection.active;
        (a.row.min(b.row)..=a.row.max(b.row))
            .map(|row| {
                (a.column.min(b.column)..=a.column.max(b.column))
                    .map(|column| {
                        let value = self
                            .draft
                            .rows
                            .get(row)
                            .map(|r| r.cells[self.visible[column]].as_str())
                            .unwrap_or("");
                        if value.contains(['\t', '\n', '\r', '"']) {
                            format!("\"{}\"", value.replace('"', "\"\""))
                        } else {
                            value.into()
                        }
                    })
                    .collect::<Vec<_>>()
                    .join("\t")
            })
            .collect::<Vec<_>>()
            .join("\n")
    }
    pub fn clear(&mut self) -> Result<(), String> {
        let a = self.selection.anchor;
        let b = self.selection.active;
        self.apply(
            a.row.min(b.row),
            a.column.min(b.column),
            vec![vec![String::new(); a.column.abs_diff(b.column) + 1]; a.row.abs_diff(b.row) + 1],
        )
    }
    pub fn fill_down(&mut self) -> Result<(), String> {
        let a = self.selection.anchor;
        let b = self.selection.active;
        let top = a.row.min(b.row);
        if top == 0 && a.row == b.row {
            return Ok(());
        }
        let source = if a.row == b.row { top - 1 } else { top };
        let start = source + 1;
        let end = a.row.max(b.row);
        let Some(item) = self.draft.rows.get(source) else {
            return Ok(());
        };
        let cells = (a.column.min(b.column)..=a.column.max(b.column))
            .map(|column| item.cells[self.visible[column]].clone())
            .collect();
        self.apply(start, a.column.min(b.column), vec![cells; end - start + 1])
    }
    pub fn row_action(&mut self, action: &str, row: usize) -> Result<(), String> {
        let mut draft = self.draft.clone();
        match action {
            "insert" => {
                if draft.rows.len() >= MAX_ROWS {
                    return Err(format!("最多 {MAX_ROWS} 行。"));
                }
                draft
                    .rows
                    .insert(row.min(draft.rows.len()), ItemRow::blank());
            }
            "duplicate" if row < draft.rows.len() => {
                if draft.rows.len() >= MAX_ROWS {
                    return Err(format!("最多 {MAX_ROWS} 行。"));
                }
                let mut item = draft.rows[row].clone();
                item.original.id = 0;
                draft.rows.insert(row + 1, item);
            }
            "delete" if row < draft.rows.len() => {
                draft.rows.remove(row);
            }
            "up" if row > 0 && row < draft.rows.len() => {
                draft.rows.swap(row, row - 1);
                self.select(row - 1, self.selection.active.column, false);
            }
            "down" if row + 1 < draft.rows.len() => {
                draft.rows.swap(row, row + 1);
                self.select(row + 1, self.selection.active.column, false);
            }
            _ => return Ok(()),
        }
        self.replace(draft);
        Ok(())
    }
    pub fn undo(&mut self) {
        self.history.undo(&mut self.draft);
    }
    pub fn redo(&mut self) {
        self.history.redo(&mut self.draft);
    }
    pub fn display(&self, row: usize, display_column: usize) -> String {
        let actual = self.visible[display_column];
        self.draft
            .rows
            .get(row)
            .map(|row| {
                let text = &row.cells[actual];
                if ITEM_COLUMNS[actual].numeric && text == "0" {
                    String::new()
                } else {
                    text.clone()
                }
            })
            .unwrap_or_default()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn hidden_columns_atomic_paste_history_and_manual_totals_survive() {
        let mut grid = InvoiceGrid::new(InvoiceDraft::demo("2026-09-16", "GRID"), 0);
        grid.draft.rows[0].cells[37] = "保留".into();
        grid.select(0, 23, false);
        grid.paste("1.23567").unwrap();
        assert_eq!(
            grid.draft.build().unwrap().items[0].total_price.to_string(),
            "1235.67"
        );
        grid.select(0, 24, false);
        grid.paste("1000.01").unwrap();
        assert_eq!(
            grid.draft.build().unwrap().items[0].price_calculation_mode,
            "LineAmountDriven"
        );
        let before = grid.draft.clone();
        assert!(grid.paste("NaN").is_err());
        assert_eq!(grid.draft, before);
        grid.undo();
        assert_eq!(grid.draft.rows[0].cells[24], "1235.67");
        grid.redo();
        assert_eq!(grid.draft, before);
        assert_eq!(grid.draft.rows[0].cells[37], "保留");
        grid.select(0, 20, false);
        grid.paste("123.45").unwrap();
        assert_eq!(
            grid.draft.build().unwrap().items[0].gw_total.to_string(),
            "123.45"
        );
    }
    #[test]
    fn all_columns_roundtrip_and_visible_paste_skip_hidden_fields() {
        assert_eq!(COLUMN_COUNT, 38);
        let mut grid = InvoiceGrid::new(InvoiceDraft::new("2026-09-16"), 0);
        grid.set_visible(1, false).unwrap();
        grid.paste("PO-1\tshirt").unwrap();
        assert_eq!(grid.draft.rows[0].cells[0], "PO-1");
        assert_eq!(grid.draft.rows[0].cells[1], "");
        assert_eq!(grid.draft.rows[0].cells[2], "shirt");
        grid.select(0, 0, false);
        grid.select(0, 1, true);
        assert_eq!(grid.copy(), "PO-1\tshirt");
        grid.row_action("duplicate", 0).unwrap();
        assert_eq!(grid.draft.rows.len(), 2);
        grid.undo();
        assert_eq!(grid.draft.rows.len(), 1);
    }
}
