import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import {
  applyDetailTableBorderToColumns,
  clearDetailTableColumnBorders,
  createDetailTableSideBand,
  distributeDetailTableColumnWidths,
  resizeAdjacentDetailTableColumnWidths,
} from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { normalizeNumber } from "./reportDesignerPropertiesModel.ts";
import { DesignerCheckbox, ColumnWidthStrip, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";

export function ReportDesignerDetailTableLayoutProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const detailColumnWidthTotal = Math.round(block.columns.reduce((sum, column) => sum + Math.max(0, column.widthMm), 0) * 10) / 10;

  return (
    <>
      <div className="new-report-property-readout">
        <span>数据源</span>
        <strong>商品明细 · 每条商品一行</strong>
      </div>
      <label>
        <span>明细标题</span>
        <input value={block.title ?? ""} onChange={(event) => onCommit({ ...block, title: event.target.value })} />
      </label>
      {block.sideBand ? (
        <label>
          <span>明细区宽度(mm)</span>
          <input
            type="number"
            min={40}
            max={240}
            step={1}
            value={block.detailWidthMm ?? 132}
            onChange={(event) => onCommit({ ...block, detailWidthMm: normalizeNumber(event.target.value, block.detailWidthMm ?? 132) })}
          />
        </label>
      ) : null}
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>表格版式</strong>
          <div className="new-report-detail-column-actions">
            <button className="command-button secondary" type="button" onClick={() => onCommit(distributeDetailTableColumnWidths(block))}>
              等分列宽
            </button>
            <button className="command-button secondary" type="button" onClick={() => onCommit(applyDetailTableBorderToColumns(block))}>
              统一边框
            </button>
            <button className="command-button secondary" type="button" onClick={() => onCommit(clearDetailTableColumnBorders(block))}>
              恢复默认
            </button>
          </div>
        </div>
        <div className="new-report-property-readout">
          <span>列总宽</span>
          <strong>{detailColumnWidthTotal}mm / {block.columns.length} 列</strong>
        </div>
        <ColumnWidthStrip
          columns={block.columns.map((column, index) => ({
            id: column.id,
            title: column.title || `列 ${index + 1}`,
            width: column.widthMm,
          }))}
          minWidth={8}
          unit="mm"
          onResizeBoundary={(leftColumnId, delta) => onCommit(resizeAdjacentDetailTableColumnWidths(block, leftColumnId, delta))}
        />
        <div className="new-report-designer-muted">拖动列之间的分隔线调整宽度，松开后应用；方向键可微调。</div>
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>打印分页</strong>
        </div>
        <div className="new-report-property-grid">
          <DesignerCheckbox checked={block.print.repeatHeaderOnPageBreak}
              onChange={(checked) =>
                onCommit({
                  ...block,
                  print: {
                    ...block.print,
                    repeatHeaderOnPageBreak: checked,
                  },
                })
              }>跨页重复表头</DesignerCheckbox>
          <DesignerCheckbox checked={block.print.keepRowsTogether}
              onChange={(checked) =>
                onCommit({
                  ...block,
                  print: {
                    ...block.print,
                    keepRowsTogether: checked,
                  },
                })
              }>明细行避免截断</DesignerCheckbox>
        </div>
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>非循环侧栏</strong>
          {block.sideBand ? (
            <button className="command-button secondary" type="button" onClick={() => onCommit({ ...block, sideBand: undefined })}>
              移除
            </button>
          ) : (
            <button className="command-button secondary" type="button" onClick={() => onCommit({ ...block, sideBand: createDetailTableSideBand() })}>
              添加唛头栏
            </button>
          )}
        </div>
        {block.sideBand ? (
          <div className="new-report-property-grid">
            <label>
              <span>侧栏标题</span>
              <input
                value={block.sideBand.title}
                onChange={(event) => onCommit({ ...block, sideBand: { ...block.sideBand!, title: event.target.value } })}
              />
            </label>
            <label>
              <span>宽度(mm)</span>
              <input
                type="number"
                min={16}
                max={120}
                step={1}
                value={block.sideBand.widthMm}
                onChange={(event) =>
                  onCommit({
                    ...block,
                    sideBand: { ...block.sideBand!, widthMm: normalizeNumber(event.target.value, block.sideBand!.widthMm) },
                  })
                }
              />
            </label>
            <label>
              <span>内容类型</span>
              <select
                value={block.sideBand.contentKind}
                onChange={(event) =>
                  onCommit({
                    ...block,
                    sideBand: { ...block.sideBand!, contentKind: event.target.value === "Text" ? "Text" : "Field" },
                  })
                }
              >
                <option value="Field">字段</option>
                <option value="Text">固定文本</option>
              </select>
            </label>
            {block.sideBand.contentKind === "Field" ? (
              <FieldPathInput
                className="new-report-property-wide"
                label="字段"
                value={block.sideBand.fieldPath}
                fieldGroups={fieldGroups}
                onChange={(fieldPath) =>
                  onCommit({
                    ...block,
                    sideBand: { ...block.sideBand!, fieldPath },
                  })
                }
              />
            ) : (
              <label className="new-report-property-wide">
                <span>固定内容</span>
                <textarea
                  rows={5}
                  value={block.sideBand.text}
                  onChange={(event) => onCommit({ ...block, sideBand: { ...block.sideBand!, text: event.target.value } })}
                />
              </label>
            )}
            <div className="new-report-property-wide">
              <div className="new-report-designer-muted">侧栏样式</div>
              <TextStyleEditor style={block.sideBand.style} onChange={(style) => onCommit({ ...block, sideBand: { ...block.sideBand!, style } })} />
            </div>
          </div>
        ) : (
          <div className="new-report-designer-muted">用于截图这类左侧唛头/备注区域，内容不随商品明细循环。</div>
        )}
      </div>
    </>
  );
}
