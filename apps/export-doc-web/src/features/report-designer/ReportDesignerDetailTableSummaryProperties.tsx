import { useState } from "react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { createDetailTableSummaryRow } from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportDetailTableBlock, ReportDetailTableSummaryCell } from "./reportDesignerSchema.ts";
import { createEmptySummaryCell, normalizeNumber, normalizeSummaryContentKind } from "./reportDesignerPropertiesModel.ts";
import { BorderEditor, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";

export function ReportDesignerDetailTableSummaryProperties({
  block,
  fieldGroups,
  onCommit,
  title = "表尾合计行",
}: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
  title?: string;
}) {
  const [selectedId, setSelectedId] = useState("");
  const selected = block.columns.some(column => column.id === selectedId) ? selectedId : block.columns[0]?.id;
  const summaryLabelSpan = block.summaryRow
    ? Math.min(block.columns.length, Math.max(1, Math.floor(block.summaryRow.labelColumnSpan)))
    : 1;

  function updateSummaryCell(columnId: string, update: (cell: ReportDetailTableSummaryCell) => ReportDetailTableSummaryCell) {
    if (!block.summaryRow) {
      return;
    }

    const currentCell = block.summaryRow.cells.find((cell) => cell.columnId === columnId) ?? createEmptySummaryCell(columnId);
    const nextCell = update(currentCell);
    const exists = block.summaryRow.cells.some((cell) => cell.columnId === columnId);

    onCommit({
      ...block,
      summaryRow: {
        ...block.summaryRow,
        cells: exists
          ? block.summaryRow.cells.map((cell) => (cell.columnId === columnId ? nextCell : cell))
          : [...block.summaryRow.cells, nextCell],
      },
    });
  }

  return (
    <>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>{title}</strong>
          {block.summaryRow ? (
            <button className="command-button secondary" type="button" onClick={() => onCommit({ ...block, summaryRow: undefined })}>
              移除
            </button>
          ) : (
            <button className="command-button secondary" type="button" onClick={() => onCommit({ ...block, summaryRow: createDetailTableSummaryRow(block.columns) })}>
              添加
            </button>
          )}
        </div>
        {block.summaryRow ? (
          <div className="new-report-property-grid">
            <label>
              <span>标签</span>
              <input
                value={block.summaryRow.label}
                onChange={(event) => onCommit({ ...block, summaryRow: { ...block.summaryRow!, label: event.target.value } })}
              />
            </label>
            <label>
              <span>标签跨列</span>
              <input
                type="number"
                min={1}
                max={block.columns.length}
                step={1}
                value={summaryLabelSpan}
                onChange={(event) =>
                  onCommit({
                    ...block,
                    summaryRow: {
                      ...block.summaryRow!,
                      labelColumnSpan: Math.min(block.columns.length, Math.max(1, Math.floor(normalizeNumber(event.target.value, summaryLabelSpan)))),
                    },
                  })
                }
              />
            </label>
            <label className="new-report-property-wide"><span>设置哪一列</span><select value={selected} onChange={event => setSelectedId(event.target.value)}>
              {block.columns.map((column, index) => <option key={column.id} value={column.id}>{index + 1}. {column.title || "未命名列"}</option>)}
            </select></label>
            {block.columns.filter(column => column.id === selected).map((column) => {
              const cell = block.summaryRow!.cells.find((candidate) => candidate.columnId === column.id) ?? createEmptySummaryCell(column.id);

              return (
                <div className="new-report-summary-cell-editor" key={column.id}>
                  <strong>{column.title}</strong>
                  {block.columns.indexOf(column) < summaryLabelSpan && <small>此列内容在左侧合并区域内，显示在标签下方。</small>}
                  {cell.contentKind === "Field" && <label><span>单位/后缀</span><input maxLength={30} value={cell.suffix ?? ""} onChange={e => updateSummaryCell(column.id, current => ({ ...current, suffix: e.target.value }))} /></label>}
                  <label>
                    <span>内容</span>
                    <select
                      value={cell.contentKind}
                      onChange={(event) =>
                        updateSummaryCell(column.id, (current) => ({
                          ...current,
                          contentKind: normalizeSummaryContentKind(event.target.value),
                        }))
                      }
                    >
                      <option value="Empty">空白</option>
                      <option value="Field">字段</option>
                      <option value="Text">固定文本</option>
                    </select>
                  </label>
                  {cell.contentKind === "Field" ? (
                    <FieldPathInput
                      selectOnly
                      label="字段"
                      value={cell.fieldPath}
                      fieldGroups={fieldGroups}
                      onChange={(fieldPath) =>
                        updateSummaryCell(column.id, (current) => ({
                          ...current,
                          fieldPath,
                        }))
                      }
                    />
                  ) : null}
                  {cell.contentKind === "Field" ? <label><span>显示格式</span><select value={cell.fieldFormat ?? ""} onChange={event => updateSummaryCell(column.id,current=>({...current,fieldFormat:event.target.value === "Currency" ? "Currency" : undefined}))}><option value="">原始值</option><option value="Currency">币种与金额</option></select></label> : null}
                  {cell.contentKind === "Text" ? (
                    <label>
                      <span>固定文本</span>
                      <input
                        value={cell.text}
                        onChange={(event) =>
                          updateSummaryCell(column.id, (current) => ({
                            ...current,
                            text: event.target.value,
                          }))
                        }
                      />
                    </label>
                  ) : null}
                </div>
              );
            })}
            <details className="new-report-property-wide">
              <summary>本行文字与边框</summary>
              <TextStyleEditor style={block.summaryRow.style} onChange={(style) => onCommit({ ...block, summaryRow: { ...block.summaryRow!, style } })} />
              <BorderEditor border={block.summaryRow.border ?? block.border} onChange={border => onCommit({ ...block, summaryRow: { ...block.summaryRow!, border } })} />
              {block.summaryRow.border && <button type="button" className="command-button secondary" onClick={() => onCommit({ ...block, summaryRow: { ...block.summaryRow!, border: undefined } })}>合计行沿用表格边框</button>}
            </details>
          </div>
        ) : (
          <div className="new-report-designer-muted">用于发票总数量、总箱数、总金额等报表尾部汇总，不随明细循环重复。</div>
        )}
      </div>
    </>
  );
}
