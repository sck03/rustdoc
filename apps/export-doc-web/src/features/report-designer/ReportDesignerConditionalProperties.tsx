import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { normalizeDesignerFieldPath } from "./reportDesignerMutations.ts";
import type { ReportBlock, ReportConditionalContent, ReportConditionalRule } from "./reportDesignerSchema.ts";
import { FieldPathInput } from "./ReportDesignerPropertyControls.tsx";
import { normalizeConditionalContentKind, normalizeConditionalOperator } from "./reportDesignerPropertiesModel.ts";

export function ConditionalBlockProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: Extract<ReportBlock, { type: "Conditional" }>;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  function updateCondition(patch: Partial<ReportConditionalRule>) {
    onCommit({
      ...block,
      condition: {
        ...block.condition,
        ...patch,
      },
    });
  }

  function updateContent(patch: Partial<ReportConditionalContent>) {
    onCommit({
      ...block,
      content: {
        ...block.content,
        ...patch,
      },
    });
  }

  return (
    <div className="new-report-conditional-properties">
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>显示条件</strong>
        </div>
        <div className="new-report-property-grid">
          <FieldPathInput
            className="new-report-property-wide"
            label="条件字段"
            value={block.condition.fieldPath}
            fieldGroups={fieldGroups}
            onChange={(fieldPath) => updateCondition({ fieldPath })}
          />
          <label>
            <span>判断</span>
            <select
              value={block.condition.operator}
              onChange={(event) => updateCondition({ operator: normalizeConditionalOperator(event.target.value) })}
            >
              <option value="HasValue">有值</option>
              <option value="Equals">等于</option>
              <option value="NotEquals">不等于</option>
            </select>
          </label>
          {block.condition.operator === "Equals" || block.condition.operator === "NotEquals" ? (
            <label>
              <span>比较值</span>
              <input value={block.condition.value} onChange={(event) => updateCondition({ value: event.target.value })} />
            </label>
          ) : null}
        </div>
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>条件内容</strong>
        </div>
        <div className="new-report-property-grid">
          <label>
            <span>内容类型</span>
            <select
              value={block.content.kind}
              onChange={(event) => updateContent({ kind: normalizeConditionalContentKind(event.target.value) })}
            >
              <option value="Field">字段</option>
              <option value="Text">固定文本</option>
            </select>
          </label>
          {block.content.kind === "Field" ? (
            <>
              <label>
                <span>标签</span>
                <input value={block.content.label ?? ""} onChange={(event) => updateContent({ label: event.target.value })} />
              </label>
              <FieldPathInput
                className="new-report-property-wide"
                label="字段"
                value={block.content.fieldPath}
                fieldGroups={fieldGroups}
                onChange={(fieldPath) => updateContent({ fieldPath })}
              />
              <label className="new-report-property-wide">
                <span>占位文本</span>
                <input value={block.content.fallbackText ?? ""} onChange={(event) => updateContent({ fallbackText: event.target.value })} />
              </label>
            </>
          ) : (
            <label className="new-report-property-wide">
              <span>固定内容</span>
              <textarea rows={4} value={block.content.text} onChange={(event) => updateContent({ text: event.target.value })} />
            </label>
          )}
        </div>
      </div>
    </div>
  );
}

