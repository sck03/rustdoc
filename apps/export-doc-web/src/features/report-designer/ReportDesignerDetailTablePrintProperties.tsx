import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { createDetailTableSideBand } from "./reportDesignerBlockFactories.ts";
import type { ReportBlock, ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { normalizeNumber } from "./reportDesignerPropertiesModel.ts";
import { DesignerCheckbox, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";

export function ReportDesignerDetailTablePrintProperties({ block, fieldGroups, onCommit }: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  return <>
      <details className="new-report-detail-style-group" open>
        <summary>打印分页</summary>
        <label><span>明细标题</span><input value={block.title ?? ""} onChange={event => onCommit({ ...block, title: event.target.value })} /></label>
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
          <label>
            <span>首页明细行数</span>
            <input
              type="number"
              min={1}
              max={80}
              step={1}
              value={block.print.firstPageRows ?? ""}
              placeholder="自动"
              onChange={(event) =>
                onCommit({
                  ...block,
                  print: {
                    ...block.print,
                    firstPageRows: optionalRowLimit(event.target.value),
                  },
                })
              }
            />
          </label>
          <label>
            <span>续页明细行数</span>
            <input
              type="number"
              min={1}
              max={80}
              step={1}
              value={block.print.continuationPageRows ?? ""}
              placeholder="自动"
              onChange={(event) =>
                onCommit({
                  ...block,
                  print: {
                    ...block.print,
                    continuationPageRows: optionalRowLimit(event.target.value),
                  },
                })
              }
            />
          </label>
          <div className="new-report-designer-muted">留空时按页面高度自动分页;填写后每页最多显示该行数。</div>
        </div>
      </details>
      <details className="new-report-detail-style-group">
        <summary>唛头侧栏</summary>
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
            <label><span>明细区参考宽度(mm)</span><input type="number" min={40} max={240} step="any" value={block.detailWidthMm ?? 132} onChange={event => onCommit({ ...block, detailWidthMm: normalizeNumber(event.target.value, block.detailWidthMm ?? 132) })} /></label>
            <DesignerCheckbox checked={block.sideBand.firstPageOnly === true} onChange={checked => onCommit({...block,sideBand:{...block.sideBand!,firstPageOnly:checked}})}>唛头内容仅首页显示</DesignerCheckbox>
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
                step="any"
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
          <div className="new-report-designer-muted">唛头自动显示文字或图片，内容不随商品明细循环。</div>
        )}
      </details>
  </>;
}
function optionalRowLimit(value: string) {
  if (value.trim() === "") {
    return undefined;
  }
  const parsed = Number.parseInt(value, 10);
  return Number.isInteger(parsed) && parsed >= 1 && parsed <= 80 ? parsed : undefined;
}
