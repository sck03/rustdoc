import { ArrowDown, ArrowUp, Copy, Trash2 } from "lucide-react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { createRowColumn, normalizeDesignerFieldPath, normalizeRowColumnWidths, resizeAdjacentRowColumnWidths } from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportRowColumn } from "./reportDesignerSchema.ts";
import { BorderEditor, ColumnWidthStrip, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { normalizeBorderForEditor, normalizeNumber, readDefaultRowFieldPath } from "./reportDesignerPropertiesModel.ts";

export function RowBlockProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: Extract<ReportBlock, { type: "Row" }>;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  function commitColumns(columns: ReportRowColumn[]) {
    onCommit({
      ...block,
      columns: normalizeRowColumnWidths(columns),
    });
  }

  function updateColumn(columnId: string, update: (column: ReportRowColumn) => ReportRowColumn) {
    commitColumns(block.columns.map((column) => (column.id === columnId ? update(column) : column)));
  }

  function moveColumn(columnId: string, direction: "up" | "down") {
    const currentIndex = block.columns.findIndex((column) => column.id === columnId);
    const targetIndex = direction === "up" ? currentIndex - 1 : currentIndex + 1;
    if (currentIndex < 0 || targetIndex < 0 || targetIndex >= block.columns.length) {
      return;
    }

    const columns = [...block.columns];
    const [movedColumn] = columns.splice(currentIndex, 1);
    columns.splice(targetIndex, 0, movedColumn);
    commitColumns(columns);
  }

  function duplicateColumn(columnId: string) {
    const currentIndex = block.columns.findIndex((column) => column.id === columnId);
    const sourceColumn = block.columns[currentIndex];
    if (!sourceColumn) {
      return;
    }

    const duplicate = createRowColumn(
      sourceColumn.contentKind,
      sourceColumn.text,
      sourceColumn.fieldPath,
      sourceColumn.widthPercent,
      sourceColumn.style,
      sourceColumn.label ?? "",
    );
    commitColumns([
      ...block.columns.slice(0, currentIndex + 1),
      {
        ...duplicate,
        fallbackText: sourceColumn.fallbackText,
        border: sourceColumn.border,
      },
      ...block.columns.slice(currentIndex + 1),
    ]);
  }

  function addColumn() {
    commitColumns([...block.columns, createRowColumn("Field", "", readDefaultRowFieldPath(fieldGroups))]);
  }

  function removeColumn(columnId: string) {
    if (block.columns.length <= 1) {
      return;
    }

    commitColumns(block.columns.filter((column) => column.id !== columnId));
  }

  function updateColumnCount(value: string) {
    const nextCount = Math.max(1, Math.floor(normalizeNumber(value, block.columns.length)));
    if (nextCount === block.columns.length) {
      return;
    }

    const defaultFieldPath = readDefaultRowFieldPath(fieldGroups);
    const nextColumns = block.columns.slice(0, nextCount);
    while (nextColumns.length < nextCount) {
      nextColumns.push(createRowColumn("Field", "", defaultFieldPath));
    }

    const equalWidth = Math.round((1000 / nextCount)) / 10;
    commitColumns(nextColumns.map((column) => ({ ...column, widthPercent: equalWidth })));
  }

  return (
    <div className="new-report-row-properties">
      <div className="new-report-property-grid">
        <label>
          <span>列数</span>
          <input
            type="number"
            min={1}
            step={1}
            value={block.columns.length}
            onChange={(event) => updateColumnCount(event.target.value)}
          />
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
      <ColumnWidthStrip
        columns={block.columns.map((column, index) => ({
          id: column.id,
          title: `列 ${index + 1}`,
          width: column.widthPercent,
        }))}
        minWidth={1}
        unit="%"
        onResizeBoundary={(leftColumnId, delta) => commitColumns(resizeAdjacentRowColumnWidths(block.columns, leftColumnId, delta))}
      />
      <div className="new-report-detail-column-list">
        {block.columns.map((column, index) => (
          <div className="new-report-detail-column-card" key={column.id}>
            <div className="new-report-detail-column-title">
              <strong>列 {index + 1}</strong>
              <div className="new-report-detail-column-actions" aria-label={`行列 ${index + 1} 操作`}>
                <button className="icon-button compact-icon-button" type="button" title="左移列" disabled={index === 0} onClick={() => moveColumn(column.id, "up")}>
                  <ArrowUp size={16} aria-hidden="true" />
                </button>
                <button className="icon-button compact-icon-button" type="button" title="右移列" disabled={index >= block.columns.length - 1} onClick={() => moveColumn(column.id, "down")}>
                  <ArrowDown size={16} aria-hidden="true" />
                </button>
                <button className="icon-button compact-icon-button" type="button" title="复制列" onClick={() => duplicateColumn(column.id)}>
                  <Copy size={16} aria-hidden="true" />
                </button>
                <button className="icon-button compact-icon-button danger-icon" type="button" title="删除列" disabled={block.columns.length <= 1} onClick={() => removeColumn(column.id)}>
                  <Trash2 size={16} aria-hidden="true" />
                </button>
              </div>
            </div>
            <div className="new-report-property-grid">
              <label>
                <span>内容</span>
                <select
                  value={column.contentKind}
                  onChange={(event) => updateColumn(column.id, (current) => ({ ...current, contentKind: event.target.value === "Field" ? "Field" : "Text" }))}
                >
                  <option value="Text">固定文本</option>
                  <option value="Field">字段</option>
                </select>
              </label>
              <label>
                <span>宽度(%)</span>
                <input
                  type="number"
                  min={1}
                  max={100}
                  step={0.1}
                  value={Math.round(column.widthPercent * 10) / 10}
                  onChange={(event) => updateColumn(column.id, (current) => ({ ...current, widthPercent: normalizeNumber(event.target.value, current.widthPercent) }))}
                />
              </label>
              {column.contentKind === "Field" ? (
                <>
                  <label>
                    <span>标签</span>
                    <input value={column.label ?? ""} onChange={(event) => updateColumn(column.id, (current) => ({ ...current, label: event.target.value }))} />
                  </label>
                  <FieldPathInput
                    className="new-report-property-wide"
                    label="字段"
                    value={column.fieldPath}
                    fieldGroups={fieldGroups}
                    onChange={(fieldPath) => updateColumn(column.id, (current) => ({ ...current, fieldPath }))}
                  />
                  <label className="new-report-property-wide">
                    <span>占位文本</span>
                    <input
                      value={column.fallbackText ?? ""}
                      onChange={(event) => updateColumn(column.id, (current) => ({ ...current, fallbackText: event.target.value }))}
                    />
                  </label>
                </>
              ) : (
                <label className="new-report-property-wide">
                  <span>固定内容</span>
                  <textarea rows={4} value={column.text} onChange={(event) => updateColumn(column.id, (current) => ({ ...current, text: event.target.value }))} />
                </label>
              )}
            </div>
            <div className="new-report-designer-muted">列样式</div>
            <TextStyleEditor style={column.style} onChange={(style) => updateColumn(column.id, (current) => ({ ...current, style }))} />
            <BorderEditor border={column.border} onChange={(border) => updateColumn(column.id, (current) => ({ ...current, border }))} />
          </div>
        ))}
      </div>
      <button className="command-button secondary" type="button" onClick={addColumn}>
        新增列
      </button>
    </div>
  );
}
