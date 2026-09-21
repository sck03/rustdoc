//! HTML table occupancy rules, shared by validation and native report layout.
use super::{GridCell, ReportGridBlock};

pub struct CellPlacement<'a> {
    pub cell: &'a GridCell,
    pub row: usize,
    pub column: usize,
    pub row_span: usize,
    pub col_span: usize,
}

pub fn placements(block: &ReportGridBlock) -> Result<Vec<CellPlacement<'_>>, String> {
    let (rows, columns) = (block.rows.len(), block.columns.len());
    if rows == 0 || rows > 1000 || columns == 0 || columns > 100 {
        return Err("普通表格行列数量无效。".into());
    }
    let mut occupied = vec![vec![false; columns]; rows];
    let mut result = Vec::new();
    for (row, data) in block.rows.iter().enumerate() {
        let mut column = 0;
        for cell in &data.cells {
            while column < columns && occupied[row][column] {
                column += 1;
            }
            let row_span = usize::try_from(cell.row_span).unwrap_or(0);
            let col_span = usize::try_from(cell.col_span).unwrap_or(0);
            if row_span == 0
                || col_span == 0
                || row_span > rows - row
                || col_span > columns - column
            {
                return Err(format!("普通表格单元格 {} 的合并范围越界。", cell.id));
            }
            for cells in occupied.iter_mut().skip(row).take(row_span) {
                for value in cells.iter_mut().skip(column).take(col_span) {
                    if *value {
                        return Err(format!("普通表格单元格 {} 的合并范围重叠。", cell.id));
                    }
                    *value = true;
                }
            }
            result.push(CellPlacement {
                cell,
                row,
                column,
                row_span,
                col_span,
            });
            column += col_span;
        }
        if data.cells.is_empty() && occupied[row].iter().any(|value| !value) {
            return Err("普通表格空行必须由跨行合并单元格完整覆盖。".into());
        }
    }
    Ok(result)
}
