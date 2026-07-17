import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { normalizeDesignerFieldPath } from "./reportDesignerMutations.ts";
import type { ReportBlock } from "./reportDesignerSchema.ts";
import { FieldPathInput } from "./ReportDesignerPropertyControls.tsx";
import { normalizeAlign, normalizeImageSourceKind, normalizeNumber } from "./reportDesignerPropertiesModel.ts";

export function ImageBlockProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: Extract<ReportBlock, { type: "Image" }>;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  return (
    <div className="new-report-image-properties">
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>图片来源</strong>
        </div>
        <div className="new-report-property-grid">
          <label>
            <span>名称</span>
            <input value={block.title ?? ""} onChange={(event) => onCommit({ ...block, title: event.target.value })} />
          </label>
          <label>
            <span>来源类型</span>
            <select
              value={block.sourceKind}
              onChange={(event) => onCommit({ ...block, sourceKind: normalizeImageSourceKind(event.target.value) })}
            >
              <option value="Field">字段图片</option>
              <option value="StaticUrl">静态地址</option>
            </select>
          </label>
          {block.sourceKind === "Field" ? (
            <FieldPathInput
              className="new-report-property-wide"
              label="字段"
              value={block.fieldPath}
              fieldGroups={fieldGroups}
              onChange={(fieldPath) => onCommit({ ...block, fieldPath })}
            />
          ) : (
            <label className="new-report-property-wide">
              <span>图片地址</span>
              <textarea
                rows={3}
                value={block.url}
                onChange={(event) => onCommit({ ...block, url: event.target.value })}
              />
            </label>
          )}
          <label className="new-report-property-wide">
            <span>替代文本</span>
            <input value={block.altText ?? ""} onChange={(event) => onCommit({ ...block, altText: event.target.value })} />
          </label>
        </div>
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>尺寸与打印</strong>
        </div>
        <div className="new-report-property-grid">
          <label>
            <span>宽度(mm)</span>
            <input
              type="number"
              min={4}
              max={180}
              step={1}
              value={block.widthMm}
              onChange={(event) => onCommit({ ...block, widthMm: normalizeNumber(event.target.value, block.widthMm) })}
            />
          </label>
          <label>
            <span>高度(mm)</span>
            <input
              type="number"
              min={4}
              max={180}
              step={1}
              value={block.heightMm ?? ""}
              onChange={(event) =>
                onCommit({
                  ...block,
                  heightMm: event.target.value === "" ? undefined : normalizeNumber(event.target.value, block.heightMm ?? 24),
                })
              }
            />
          </label>
          <label>
            <span>对齐</span>
            <select value={block.align} onChange={(event) => onCommit({ ...block, align: normalizeAlign(event.target.value) })}>
              <option value="Left">左</option>
              <option value="Center">中</option>
              <option value="Right">右</option>
            </select>
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
          <label className="new-report-checkbox-label">
            <span>空源不打印</span>
            <input
              type="checkbox"
              checked={block.hideWhenSourceEmpty}
              onChange={(event) => onCommit({ ...block, hideWhenSourceEmpty: event.target.checked })}
            />
          </label>
          <label className="new-report-checkbox-label">
            <span>避免跨页拆分</span>
            <input
              type="checkbox"
              checked={block.keepTogether}
              onChange={(event) => onCommit({ ...block, keepTogether: event.target.checked })}
            />
          </label>
        </div>
      </div>
    </div>
  );
}
