import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import {
  applyGridBorderToCells,
  applyGridDefaultCellStyle,
  createGridCell,
  createGridColumns,
  createGridRow,
  distributeGridColumnWidths,
  normalizeDesignerFieldPath,
  resizeAdjacentGridColumnWidths,
} from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportGridBlock, ReportGridCell, ReportGridRow } from "./reportDesignerSchema.ts";
import { BorderEditor, ColumnWidthStrip, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { normalizeBorderForEditor, normalizeGridCellContentKind, normalizeNumber } from "./reportDesignerPropertiesModel.ts";

export function GridBlockProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: ReportGridBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  function updateCell(rowId: string, cellId: string, update: (cell: ReportGridCell) => ReportGridCell) {
    onCommit({
      ...block,
      rows: block.rows.map((row) =>
        row.id === rowId
          ? {
              ...row,
              cells: row.cells.map((cell) => (cell.id === cellId ? update(cell) : cell)),
            }
          : row,
      ),
    });
  }

  function updateRow(rowId: string, update: (row: ReportGridRow) => ReportGridRow) {
    onCommit({
      ...block,
      rows: block.rows.map((row) => (row.id === rowId ? update(row) : row)),
    });
  }

  function addRow() {
    onCommit({
      ...block,
      rows: [...block.rows, createGridRow([createGridCell("Text", "新单元格")])],
    });
  }

  function removeRow(rowId: string) {
    if (block.rows.length <= 1) {
      return;
    }

    onCommit({
      ...block,
      rows: block.rows.filter((row) => row.id !== rowId),
    });
  }

  function addCell(rowId: string) {
    updateRow(rowId, (row) => ({
      ...row,
      cells: [...row.cells, createGridCell("Text", "新单元格")],
    }));
  }

  function removeCell(rowId: string, cellId: string) {
    updateRow(rowId, (row) => ({
      ...row,
      cells: row.cells.length <= 1 ? row.cells : row.cells.filter((cell) => cell.id !== cellId),
    }));
  }

  function updateColumnWidth(columnId: string, widthPercent: number) {
    onCommit({
      ...block,
      columns: block.columns.map((column) =>
        column.id === columnId ? { ...column, widthPercent } : column,
      ),
    });
  }

  function updateColumnCount(value: string) {
    const nextCount = Math.max(1, Math.floor(normalizeNumber(value, block.columns.length)));
    if (nextCount === block.columns.length) {
      return;
    }

    const nextColumns = block.columns.slice(0, nextCount);
    while (nextColumns.length < nextCount) {
      nextColumns.push(createGridColumns(1)[0]);
    }

    const equalWidth = Math.round(1000 / nextCount) / 10;
    onCommit({
      ...block,
      columns: nextColumns.map((column) => ({ ...column, widthPercent: equalWidth })),
      rows: block.rows.map((row) => ({
        ...row,
        cells: row.cells.map((cell) => ({
          ...cell,
          colSpan: Math.min(nextCount, Math.max(1, Math.floor(cell.colSpan ?? 1))),
        })),
      })),
    });
  }

  function addColumn() {
    updateColumnCount(String(block.columns.length + 1));
  }

  function removeColumn() {
    if (block.columns.length <= 1) {
      return;
    }

    updateColumnCount(String(block.columns.length - 1));
  }

  function distributeColumns() {
    onCommit(distributeGridColumnWidths(block));
  }

  function applyDefaultStyleToCells() {
    onCommit(applyGridDefaultCellStyle(block));
  }

  function applyBorderToCells() {
    onCommit(applyGridBorderToCells(block));
  }

  function updateCheckboxOptions(rowId: string, cellId: string, value: string) {
    const options = value
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean)
      .map((line, index) => {
        const [label, optionValue] = line.split("=");
        return {
          id: `grid-option-${index + 1}`,
          label: (label ?? "").trim(),
          value: (optionValue ?? label ?? "").trim(),
        };
      });

    updateCell(rowId, cellId, (cell) => ({ ...cell, checkboxOptions: options }));
  }

  return (
    <div className="new-report-grid-properties">
      <div className="new-report-property-grid">
        <label>
          <span>标题</span>
          <input value={block.title ?? ""} onChange={(event) => onCommit({ ...block, title: event.target.value })} />
        </label>
        <label>
          <span>上距(mm)</span>
          <input
            type="number"
            min={0}
            max={30}
            step={0.5}
            value={block.marginTopMm ?? 0}
            onChange={(event) => onCommit({ ...block, marginTopMm: normalizeNumber(event.target.value, block.marginTopMm ?? 0) })}
          />
        </label>
        <label>
          <span>下距(mm)</span>
          <input
            type="number"
            min={0}
            max={30}
            step={0.5}
            value={block.marginBottomMm ?? 0}
            onChange={(event) => onCommit({ ...block, marginBottomMm: normalizeNumber(event.target.value, block.marginBottomMm ?? 0) })}
          />
        </label>
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>列宽</strong>
          <div className="new-report-detail-column-actions">
            <button className="command-button secondary" type="button" onClick={addColumn}>
              加列
            </button>
            <button className="command-button secondary" type="button" disabled={block.columns.length <= 1} onClick={removeColumn}>
              减列
            </button>
            <button className="command-button secondary" type="button" onClick={distributeColumns}>
              等分
            </button>
          </div>
        </div>
        <ColumnWidthStrip
          columns={block.columns.map((column, index) => ({
            id: column.id,
            title: `列 ${index + 1}`,
            width: column.widthPercent,
          }))}
          minWidth={1}
          unit="%"
          onResizeBoundary={(leftColumnId, delta) => onCommit(resizeAdjacentGridColumnWidths(block, leftColumnId, delta))}
        />
        <div className="new-report-property-grid">
          <label>
            <span>列数</span>
            <input
              type="number"
              min={1}
              max={24}
              step={1}
              value={block.columns.length}
              onChange={(event) => updateColumnCount(event.target.value)}
            />
          </label>
          {block.columns.map((column, index) => (
            <label key={column.id}>
              <span>列 {index + 1}(%)</span>
              <input
                type="number"
                min={1}
                max={100}
                step={0.1}
                value={Math.round(column.widthPercent * 10) / 10}
                onChange={(event) => updateColumnWidth(column.id, normalizeNumber(event.target.value, column.widthPercent))}
              />
            </label>
          ))}
        </div>
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>行与单元格</strong>
          <button className="command-button secondary" type="button" onClick={addRow}>
            新增行
          </button>
        </div>
        <div className="new-report-detail-column-list">
          {block.rows.map((row, rowIndex) => (
            <div className="new-report-detail-column-card" key={row.id}>
              <div className="new-report-detail-column-title">
                <strong>行 {rowIndex + 1}</strong>
                <div className="new-report-detail-column-actions">
                  <button className="command-button secondary" type="button" onClick={() => addCell(row.id)}>
                    新增格
                  </button>
                  <button className="command-button secondary" type="button" disabled={block.rows.length <= 1} onClick={() => removeRow(row.id)}>
                    删除行
                  </button>
                </div>
              </div>
              <label>
                <span>行高(mm)</span>
                <input
                  type="number"
                  min={2}
                  max={80}
                  step={0.5}
                  value={row.heightMm ?? 9}
                  onChange={(event) => updateRow(row.id, (current) => ({ ...current, heightMm: normalizeNumber(event.target.value, current.heightMm ?? 9) }))}
                />
              </label>
              <div className="new-report-detail-column-list">
                {row.cells.map((cell, cellIndex) => (
                  <div className="new-report-summary-cell-editor" key={cell.id}>
                    <div className="new-report-detail-column-title">
                      <strong>格 {cellIndex + 1}</strong>
                      <button className="command-button secondary" type="button" disabled={row.cells.length <= 1} onClick={() => removeCell(row.id, cell.id)}>
                        删除格
                      </button>
                    </div>
                    <div className="new-report-property-grid">
                      <label>
                        <span>内容</span>
                        <select
                          value={cell.contentKind}
                          onChange={(event) =>
                            updateCell(row.id, cell.id, (current) => ({
                              ...current,
                              contentKind: normalizeGridCellContentKind(event.target.value),
                            }))
                          }
                        >
                          <option value="Text">固定文本</option>
                          <option value="Field">字段</option>
                          <option value="CheckboxGroup">勾选组</option>
                        </select>
                      </label>
                      <label>
                        <span>横跨列</span>
                        <input
                          type="number"
                          min={1}
                          max={block.columns.length}
                          step={1}
                          value={cell.colSpan ?? 1}
                          onChange={(event) => updateCell(row.id, cell.id, (current) => ({ ...current, colSpan: Math.floor(normalizeNumber(event.target.value, current.colSpan ?? 1)) }))}
                        />
                      </label>
                      <label>
                        <span>纵跨行</span>
                        <input
                          type="number"
                          min={1}
                          max={block.rows.length}
                          step={1}
                          value={cell.rowSpan ?? 1}
                          onChange={(event) => updateCell(row.id, cell.id, (current) => ({ ...current, rowSpan: Math.floor(normalizeNumber(event.target.value, current.rowSpan ?? 1)) }))}
                        />
                      </label>
                      <label className="new-report-checkbox-label">
                        <span>竖排</span>
                        <input
                          type="checkbox"
                          checked={Boolean(cell.verticalText)}
                          onChange={(event) => updateCell(row.id, cell.id, (current) => ({ ...current, verticalText: event.target.checked }))}
                        />
                      </label>
                    </div>
                    {cell.contentKind === "Text" ? (
                      <label className="new-report-property-wide">
                        <span>文本</span>
                        <textarea
                          rows={2}
                          value={cell.text}
                          onChange={(event) => updateCell(row.id, cell.id, (current) => ({ ...current, text: event.target.value }))}
                        />
                      </label>
                    ) : null}
                    {cell.contentKind === "Field" || cell.contentKind === "CheckboxGroup" ? (
                      <FieldPathInput
                        className="new-report-property-wide"
                        label={cell.contentKind === "CheckboxGroup" ? "判断字段" : "字段"}
                        value={cell.fieldPath}
                        fieldGroups={fieldGroups}
                        onChange={(fieldPath) => updateCell(row.id, cell.id, (current) => ({ ...current, fieldPath }))}
                      />
                    ) : null}
                    {cell.contentKind === "Field" ? (
                      <label>
                        <span>标签</span>
                        <input
                          value={cell.label ?? ""}
                          onChange={(event) => updateCell(row.id, cell.id, (current) => ({ ...current, label: event.target.value }))}
                        />
                      </label>
                    ) : null}
                    {cell.contentKind === "CheckboxGroup" ? (
                      <label className="new-report-property-wide">
                        <span>勾选项（每行: 显示=匹配值）</span>
                        <textarea
                          rows={4}
                          value={(cell.checkboxOptions ?? []).map((option) => `${option.label}=${option.value}`).join("\n")}
                          onChange={(event) => updateCheckboxOptions(row.id, cell.id, event.target.value)}
                        />
                      </label>
                    ) : null}
                    <TextStyleEditor
                      style={cell.style}
                      onChange={(style) => updateCell(row.id, cell.id, (current) => ({ ...current, style }))}
                    />
                    <BorderEditor
                      border={cell.border ?? block.border}
                      onChange={(border) => updateCell(row.id, cell.id, (current) => ({ ...current, border }))}
                    />
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>默认单元格样式</strong>
          <div className="new-report-detail-column-actions">
            <button className="command-button secondary" type="button" onClick={applyDefaultStyleToCells}>
              套用样式
            </button>
            <button className="command-button secondary" type="button" onClick={applyBorderToCells}>
              套用边框
            </button>
          </div>
        </div>
        <TextStyleEditor style={block.defaultCellStyle} onChange={(defaultCellStyle) => onCommit({ ...block, defaultCellStyle })} />
      </div>
      <BorderEditor border={block.border} onChange={(border) => onCommit({ ...block, border })} />
    </div>
  );
}
