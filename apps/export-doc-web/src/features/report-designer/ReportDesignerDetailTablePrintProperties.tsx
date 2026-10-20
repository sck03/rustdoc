import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { createDetailTableSideBand } from "./reportDesignerBlockFactories.ts";
import type { ReportBlock, ReportDetailTableBlock } from "./reportDesignerSchema.ts";
import { CommitTextField, DesignerCheckbox, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { NumberField } from "./ReportDesignerV3InspectorControls.tsx";

export function ReportDesignerDetailTablePrintProperties({ block, fieldGroups, onCommit }: {
  block: ReportDetailTableBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  return <>
      <details className="new-report-detail-style-group" open>
        <summary>打印分页</summary>
        <label><span>明细标题</span><CommitTextField value={block.title ?? ""} onCommit={title => onCommit({ ...block, title })} /></label>
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
          <NumberField label="首页明细行数" optional value={block.print.firstPageRows} min={1} max={80} step={1} precision={0} onCommit={firstPageRows => onCommit({ ...block, print: { ...block.print, firstPageRows } })} />
          <NumberField label="续页明细行数" optional value={block.print.continuationPageRows} min={1} max={80} step={1} precision={0} onCommit={continuationPageRows => onCommit({ ...block, print: { ...block.print, continuationPageRows } })} />
          <div className="new-report-designer-muted">留空时按页面高度自动分页;填写后每页最多显示该行数。</div>
          <small>明细按整行分页；单行内容超过一页时，请调整列宽或字号。</small>
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
            <DesignerCheckbox checked={block.sideBand.firstPageOnly === true} onChange={checked => onCommit({...block,sideBand:{...block.sideBand!,firstPageOnly:checked}})}>唛头内容仅首页显示</DesignerCheckbox>
            <label>
              <span>侧栏标题</span>
              <CommitTextField
                value={block.sideBand.title}
                onCommit={title => onCommit({ ...block, sideBand: { ...block.sideBand!, title } })}
              />
            </label>
            <NumberField label="宽度(mm)" min={16} max={120} value={block.sideBand.widthMm} onCommit={widthMm => onCommit({ ...block, sideBand: { ...block.sideBand!, widthMm } })} />
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
                <CommitTextField multiline
                  rows={5}
                  value={block.sideBand.text}
                  onCommit={text => onCommit({ ...block, sideBand: { ...block.sideBand!, text } })}
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
