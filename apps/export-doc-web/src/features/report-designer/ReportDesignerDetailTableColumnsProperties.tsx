import type { DragEvent } from "react";
import { ArrowDown, ArrowUp, Copy, Trash2 } from "lucide-react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import {
  createDetailTableCellContent,
  createDetailTableColumn,
  duplicateDetailTableColumn,
  moveDetailTableColumn,
  removeDetailTableColumn,
  reorderDetailTableColumn,
} from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportDetailTableBlock, ReportDetailTableCellContent, ReportDetailTableColumn } from "./reportDesignerSchema.ts";
import {
  createEmptyGroupFooterCell,
  createEmptySummaryCell,
  normalizeAlign,
  normalizeDetailCellPartKind,
  normalizeNumber,
} from "./reportDesignerPropertiesModel.ts";
import { BorderEditor, FieldPathInput } from "./ReportDesignerPropertyControls.tsx";

export function ReportDesignerDetailTableColumnsProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  function updateColumn(columnId: string, update: (column: ReportDetailTableColumn) => ReportDetailTableColumn) {
    onCommit({
      ...block,
      columns: block.columns.map((column) => (column.id === columnId ? update(column) : column)),
    });
  }

  function addColumnContentPart(columnId: string, kind: ReportDetailTableCellContent["kind"]) {
    updateColumn(columnId, (column) => ({
      ...column,
      contentKind: "Composite",
      content: [...(column.content ?? []), createDetailTableCellContent(kind)],
    }));
  }

  function updateColumnContentPart(
    columnId: string,
    partId: string,
    update: (part: ReportDetailTableCellContent) => ReportDetailTableCellContent,
  ) {
    updateColumn(columnId, (column) => ({
      ...column,
      content: (column.content ?? []).map((part) => (part.id === partId ? update(part) : part)),
    }));
  }

  function removeColumnContentPart(columnId: string, partId: string) {
    updateColumn(columnId, (column) => ({
      ...column,
      content: (column.content ?? []).filter((part) => part.id !== partId),
    }));
  }

  function handleColumnDragStart(event: DragEvent<HTMLDivElement>, columnId: string) {
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("application/x-exportdoc-detail-column", columnId);
  }

  function handleColumnDragOver(event: DragEvent<HTMLDivElement>) {
    if (event.dataTransfer.types.includes("application/x-exportdoc-detail-column")) {
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
    }
  }

  function handleColumnDrop(event: DragEvent<HTMLDivElement>, targetColumnId: string) {
    const sourceColumnId = event.dataTransfer.getData("application/x-exportdoc-detail-column");
    if (!sourceColumnId) {
      return;
    }

    event.preventDefault();
    onCommit(reorderDetailTableColumn(block, sourceColumnId, targetColumnId));
  }

  function addColumn() {
    const column = createDetailTableColumn();
    onCommit({
      ...block,
      columns: [...block.columns, column],
      summaryRow: block.summaryRow
        ? {
            ...block.summaryRow,
            cells: [...block.summaryRow.cells, createEmptySummaryCell(column.id)],
          }
        : undefined,
      grouping: block.grouping
        ? {
            ...block.grouping,
            footer: block.grouping.footer
              ? {
                  ...block.grouping.footer,
                  cells: [...block.grouping.footer.cells, createEmptyGroupFooterCell(column.id)],
                }
              : undefined,
          }
        : undefined,
    });
  }

  return (
    <>
      <div className="new-report-detail-column-list">
        {block.columns.map((column, index) => (
          <div
            className="new-report-detail-column-card"
            key={column.id}
            draggable
            onDragStart={(event) => handleColumnDragStart(event, column.id)}
            onDragOver={handleColumnDragOver}
            onDrop={(event) => handleColumnDrop(event, column.id)}
          >
            <div className="new-report-detail-column-title">
              <strong>列 {index + 1}</strong>
              <div className="new-report-detail-column-actions" aria-label={`列 ${index + 1} 操作`}>
                <button
                  className="icon-button compact-icon-button"
                  type="button"
                  title="上移列"
                  disabled={index === 0}
                  onClick={() => onCommit(moveDetailTableColumn(block, column.id, "up"))}
                >
                  <ArrowUp size={16} aria-hidden="true" />
                </button>
                <button
                  className="icon-button compact-icon-button"
                  type="button"
                  title="下移列"
                  disabled={index >= block.columns.length - 1}
                  onClick={() => onCommit(moveDetailTableColumn(block, column.id, "down"))}
                >
                  <ArrowDown size={16} aria-hidden="true" />
                </button>
                <button
                  className="icon-button compact-icon-button"
                  type="button"
                  title="复制列"
                  onClick={() => onCommit(duplicateDetailTableColumn(block, column.id))}
                >
                  <Copy size={16} aria-hidden="true" />
                </button>
                <button
                  className="icon-button compact-icon-button danger-icon"
                  type="button"
                  title="删除列"
                  disabled={block.columns.length <= 1}
                  onClick={() => onCommit(removeDetailTableColumn(block, column.id))}
                >
                  <Trash2 size={16} aria-hidden="true" />
                </button>
              </div>
            </div>
            <div className="new-report-property-grid">
              <label>
                <span>标题</span>
                <input value={column.title} onChange={(event) => updateColumn(column.id, (current) => ({ ...current, title: event.target.value }))} />
              </label>
              <label>
                <span>跨列表头</span>
                <input
                  value={column.headerGroupTitle ?? ""}
                  onChange={(event) => updateColumn(column.id, (current) => ({ ...current, headerGroupTitle: event.target.value }))}
                />
              </label>
              <label>
                <span>跨列数</span>
                <input
                  type="number"
                  min={1}
                  max={block.columns.length}
                  step={1}
                  value={column.headerGroupSpan ?? 1}
                  onChange={(event) =>
                    updateColumn(column.id, (current) => ({
                      ...current,
                      headerGroupSpan: Math.max(1, Math.floor(normalizeNumber(event.target.value, current.headerGroupSpan ?? 1))),
                    }))
                  }
                />
              </label>
              <label>
                <span>宽度(mm)</span>
                <input
                  type="number"
                  min={8}
                  max={180}
                  step={1}
                  value={column.widthMm}
                  onChange={(event) =>
                    updateColumn(column.id, (current) => ({
                      ...current,
                      widthMm: normalizeNumber(event.target.value, current.widthMm),
                    }))
                  }
                />
              </label>
              <label>
                <span>内容</span>
                <select
                  value={column.contentKind ?? "Field"}
                  onChange={(event) =>
                    updateColumn(column.id, (current) => ({
                      ...current,
                      contentKind: event.target.value === "Composite" ? "Composite" : "Field",
                      content:
                        event.target.value === "Composite" && (!current.content || current.content.length === 0)
                          ? [createDetailTableCellContent("Field")]
                          : current.content,
                    }))
                  }
                >
                  <option value="Field">单字段</option>
                  <option value="Composite">组合内容</option>
                </select>
              </label>
              {column.contentKind === "Composite" ? null : (
                <FieldPathInput
                  className="new-report-property-wide"
                  label="字段"
                  value={column.fieldPath}
                  fieldGroups={fieldGroups}
                  onChange={(fieldPath) =>
                    updateColumn(column.id, (current) => ({
                      ...current,
                      fieldPath,
                    }))
                  }
                />
              )}
              <label>
                <span>对齐</span>
                <select
                  value={column.align}
                  onChange={(event) => updateColumn(column.id, (current) => ({ ...current, align: normalizeAlign(event.target.value) }))}
                >
                  <option value="Left">左</option>
                  <option value="Center">中</option>
                  <option value="Right">右</option>
                </select>
              </label>
            </div>
            {column.contentKind === "Composite" ? (
              <div className="new-report-detail-style-group">
                <div className="new-report-detail-column-title">
                  <strong>组合内容</strong>
                  <div className="new-report-detail-column-actions">
                    <button className="command-button secondary" type="button" onClick={() => addColumnContentPart(column.id, "Text")}>
                      文本
                    </button>
                    <button className="command-button secondary" type="button" onClick={() => addColumnContentPart(column.id, "Field")}>
                      字段
                    </button>
                    <button className="command-button secondary" type="button" onClick={() => addColumnContentPart(column.id, "LineBreak")}>
                      换行
                    </button>
                  </div>
                </div>
                <div className="new-report-detail-column-list">
                  {(column.content ?? []).map((part, partIndex) => (
                    <div className="new-report-summary-cell-editor" key={part.id}>
                      <strong>片段 {partIndex + 1}</strong>
                      <label>
                        <span>类型</span>
                        <select
                          value={part.kind}
                          onChange={(event) =>
                            updateColumnContentPart(column.id, part.id, (current) => ({
                              ...current,
                              kind: normalizeDetailCellPartKind(event.target.value),
                            }))
                          }
                        >
                          <option value="Text">固定文本</option>
                          <option value="Field">明细字段</option>
                          <option value="LineBreak">换行</option>
                        </select>
                      </label>
                      {part.kind === "Text" ? (
                        <label>
                          <span>文本</span>
                          <input
                            value={part.text}
                            onChange={(event) =>
                              updateColumnContentPart(column.id, part.id, (current) => ({
                                ...current,
                                text: event.target.value,
                              }))
                            }
                          />
                        </label>
                      ) : null}
                      {part.kind === "Field" ? (
                        <FieldPathInput
                          label="字段"
                          value={part.fieldPath}
                          fieldGroups={fieldGroups}
                          onChange={(fieldPath) =>
                            updateColumnContentPart(column.id, part.id, (current) => ({
                              ...current,
                              fieldPath,
                            }))
                          }
                        />
                      ) : null}
                      <button className="command-button secondary" type="button" onClick={() => removeColumnContentPart(column.id, part.id)}>
                        删除片段
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
            <div className="new-report-detail-style-group">
              <div className="new-report-designer-muted">列边框覆盖</div>
              <BorderEditor border={column.border ?? block.border} onChange={(border) => updateColumn(column.id, (current) => ({ ...current, border }))} />
            </div>
          </div>
        ))}
      </div>
      <button className="command-button secondary" type="button" onClick={addColumn}>
        新增列
      </button>
    </>
  );
}
