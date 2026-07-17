import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { createDetailTableGroupFooter, createDetailTableGrouping } from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportDetailTableBlock, ReportDetailTableGroupFooterCell } from "./reportDesignerSchema.ts";
import {
  createEmptyGroupFooterCell,
  filterDetailItemFieldGroups,
  normalizeGroupFooterContentKind,
  normalizeNumber,
} from "./reportDesignerPropertiesModel.ts";
import { FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";

export function ReportDesignerDetailTableGroupingProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const groupFooterLabelSpan = block.grouping?.footer
    ? Math.min(block.columns.length, Math.max(1, Math.floor(block.grouping.footer.labelColumnSpan)))
    : 1;
  const detailItemFieldGroups = filterDetailItemFieldGroups(fieldGroups);

  function updateGroupFooterCell(columnId: string, update: (cell: ReportDetailTableGroupFooterCell) => ReportDetailTableGroupFooterCell) {
    if (!block.grouping?.footer) {
      return;
    }

    const currentCell = block.grouping.footer.cells.find((cell) => cell.columnId === columnId) ?? createEmptyGroupFooterCell(columnId);
    const nextCell = update(currentCell);
    const exists = block.grouping.footer.cells.some((cell) => cell.columnId === columnId);

    onCommit({
      ...block,
      grouping: {
        ...block.grouping,
        footer: {
          ...block.grouping.footer,
          cells: exists
            ? block.grouping.footer.cells.map((cell) => (cell.columnId === columnId ? nextCell : cell))
            : [...block.grouping.footer.cells, nextCell],
        },
      },
    });
  }

  return (
    <>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>分组表头</strong>
          {block.grouping ? (
            <button className="command-button secondary" type="button" onClick={() => onCommit({ ...block, grouping: undefined })}>
              移除
            </button>
          ) : (
            <button className="command-button secondary" type="button" onClick={() => onCommit({ ...block, grouping: createDetailTableGrouping() })}>
              添加分组
            </button>
          )}
        </div>
        {block.grouping ? (
          <div className="new-report-property-grid">
            <FieldPathInput
              className="new-report-property-wide"
              label="分组字段"
              value={block.grouping.fieldPath}
              fieldGroups={fieldGroups}
              onChange={(fieldPath) => onCommit({ ...block, grouping: { ...block.grouping!, fieldPath } })}
            />
            <label>
              <span>标题</span>
              <input
                value={block.grouping.label}
                onChange={(event) => onCommit({ ...block, grouping: { ...block.grouping!, label: event.target.value } })}
              />
            </label>
            <label className="new-report-checkbox-label">
              <span>显示字段值</span>
              <input
                type="checkbox"
                checked={block.grouping.showFieldValue}
                onChange={(event) => onCommit({ ...block, grouping: { ...block.grouping!, showFieldValue: event.target.checked } })}
              />
            </label>
            <label className="new-report-checkbox-label">
              <span>与后续明细靠拢</span>
              <input
                type="checkbox"
                checked={block.grouping.keepTogether}
                onChange={(event) => onCommit({ ...block, grouping: { ...block.grouping!, keepTogether: event.target.checked } })}
              />
            </label>
            <label className="new-report-checkbox-label">
              <span>每组另起页</span>
              <input
                type="checkbox"
                checked={Boolean(block.grouping.pageBreakBefore)}
                onChange={(event) => onCommit({ ...block, grouping: { ...block.grouping!, pageBreakBefore: event.target.checked } })}
              />
            </label>
            <div className="new-report-property-wide">
              <div className="new-report-designer-muted">分组表头样式</div>
              <TextStyleEditor style={block.grouping.style} onChange={(style) => onCommit({ ...block, grouping: { ...block.grouping!, style } })} />
            </div>
          </div>
        ) : (
          <div className="new-report-designer-muted">用于按品类、箱号或唛头字段变化插入组标题行；数据需按分组字段排序。</div>
        )}
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>分组小计</strong>
          {block.grouping ? (
            block.grouping.footer ? (
              <button className="command-button secondary" type="button" onClick={() => onCommit({ ...block, grouping: { ...block.grouping!, footer: undefined } })}>
                移除
              </button>
            ) : (
              <button className="command-button secondary" type="button" onClick={() => onCommit({ ...block, grouping: { ...block.grouping!, footer: createDetailTableGroupFooter(block.columns) } })}>
                添加小计
              </button>
            )
          ) : null}
        </div>
        {!block.grouping ? (
          <div className="new-report-designer-muted">先添加分组表头，再配置每组结束时输出的小计行。</div>
        ) : block.grouping.footer ? (
          <div className="new-report-property-grid">
            <label>
              <span>标签</span>
              <input
                value={block.grouping.footer.label}
                onChange={(event) => onCommit({ ...block, grouping: { ...block.grouping!, footer: { ...block.grouping!.footer!, label: event.target.value } } })}
              />
            </label>
            <label>
              <span>标签跨列</span>
              <input
                type="number"
                min={1}
                max={block.columns.length}
                step={1}
                value={groupFooterLabelSpan}
                onChange={(event) =>
                  onCommit({
                    ...block,
                    grouping: {
                      ...block.grouping!,
                      footer: {
                        ...block.grouping!.footer!,
                        labelColumnSpan: Math.min(block.columns.length, Math.max(1, Math.floor(normalizeNumber(event.target.value, groupFooterLabelSpan)))),
                      },
                    },
                  })
                }
              />
            </label>
            {block.columns.slice(groupFooterLabelSpan).map((column) => {
              const cell = block.grouping!.footer!.cells.find((candidate) => candidate.columnId === column.id) ?? createEmptyGroupFooterCell(column.id);

              return (
                <div className="new-report-summary-cell-editor" key={column.id}>
                  <strong>{column.title}</strong>
                  <label>
                    <span>内容</span>
                    <select
                      value={cell.contentKind}
                      onChange={(event) =>
                        updateGroupFooterCell(column.id, (current) => {
                          const contentKind = normalizeGroupFooterContentKind(event.target.value);
                          return {
                            ...current,
                            contentKind,
                            fieldPath: contentKind === "Sum" ? current.fieldPath || column.fieldPath : current.fieldPath,
                          };
                        })
                      }
                    >
                      <option value="Empty">空白</option>
                      <option value="Sum">求和</option>
                      <option value="Count">计数</option>
                      <option value="Text">固定文本</option>
                    </select>
                  </label>
                  {cell.contentKind === "Sum" ? (
                    <FieldPathInput
                      label="求和字段"
                      value={cell.fieldPath}
                      fieldGroups={detailItemFieldGroups}
                      onChange={(fieldPath) =>
                        updateGroupFooterCell(column.id, (current) => ({
                          ...current,
                          fieldPath,
                        }))
                      }
                    />
                  ) : null}
                  {cell.contentKind === "Text" ? (
                    <label>
                      <span>固定文本</span>
                      <input
                        value={cell.text}
                        onChange={(event) =>
                          updateGroupFooterCell(column.id, (current) => ({
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
            <div className="new-report-property-wide">
              <div className="new-report-designer-muted">小计行样式</div>
              <TextStyleEditor style={block.grouping.footer.style} onChange={(style) => onCommit({ ...block, grouping: { ...block.grouping!, footer: { ...block.grouping!.footer!, style } } })} />
            </div>
          </div>
        ) : (
          <div className="new-report-designer-muted">适合按品类、箱号或 HS Code 分组后输出每组数量、金额或行数小计；数据需按分组字段排序。</div>
        )}
      </div>
    </>
  );
}
