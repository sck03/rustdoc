import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBlock, ReportConditionalContent, ReportConditionalRule } from "./reportDesignerSchema.ts";
import { CommitTextField, DesignerCheckbox, FieldPathInput } from "./ReportDesignerPropertyControls.tsx";
import { normalizeConditionalContentKind } from "./reportDesignerPropertiesModel.ts";
import { MAX_CONDITIONAL_RULES, conditionNeedsValue, conditionOperatorOptions, conditionRuleIssue, conditionRules, type ReportComparisonType } from "./reportDesignerConditions.ts";

export function ConditionalBlockProperties({
  block,
  fieldGroups,
  onCommit,
}: {
  block: Extract<ReportBlock, { type: "Conditional" }>;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
}) {
  const rules = conditionRules(block);
  const documentFields = fieldGroups.map(group => ({ ...group, fields: group.fields.filter(field => !field.value.startsWith("item.") && !["doc_seal_path", "customs_seal_path"].includes(field.value)) })).filter(group => group.fields.length);
  function commitRules(next: ReportConditionalRule[]) {
    onCommit({ ...block, condition: next[0], additionalConditions: next.slice(1) });
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
        <label><span>规则组合</span><select value={block.matchMode ?? "All"} onChange={event => onCommit({ ...block, matchMode: event.target.value === "Any" ? "Any" : "All" })}>
          <option value="All">全部满足（并且）</option><option value="Any">任一满足（或者）</option>
        </select></label>
        {rules.map((rule, index) => <div key={index} className="new-report-detail-style-group" role="group" aria-label={`条件 ${index + 1}`}>
          <div className="new-report-detail-column-title"><strong>条件 {index + 1}</strong>
            {rules.length > 1 ? <button type="button" className="command-button secondary" aria-label={`删除条件 ${index + 1}`} onClick={() => commitRules(rules.filter((_, position) => position !== index))}>删除</button> : null}
          </div>
          <ConditionRuleProperties rule={rule} fieldGroups={documentFields} onChange={patch => commitRules(rules.map((item, position) => position === index ? { ...item, ...patch } : item))} />
        </div>)}
        <button type="button" className="command-button secondary" disabled={rules.length >= MAX_CONDITIONAL_RULES} onClick={() => commitRules([...rules, { fieldPath: block.condition.fieldPath, operator: "HasValue", value: "" }])}>添加条件</button>
        <p className="report-designer-v3-help">最多 {MAX_CONDITIONAL_RULES} 条规则；满足后才输出下方内容及边框。“有内容”排除空白和未勾选值。画布保留组件便于编辑，实际效果请查看单据预览。</p>
      </div>
      <div className="new-report-detail-style-group">
        <div className="new-report-detail-column-title">
          <strong>显示内容</strong>
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
                <CommitTextField value={block.content.label ?? ""} onCommit={(label) => updateContent({ label })} />
              </label>
              <FieldPathInput
                className="new-report-property-wide"
                label="字段"
                value={block.content.fieldPath}
                fieldGroups={documentFields}
                selectOnly
                onChange={(fieldPath) => updateContent({ fieldPath })}
              />
              <label className="new-report-property-wide">
                <span>占位文本</span>
                <CommitTextField value={block.content.fallbackText ?? ""} onCommit={(fallbackText) => updateContent({ fallbackText })} />
              </label>
            </>
          ) : (
            <label className="new-report-property-wide">
              <span>固定内容</span>
              <CommitTextField value={block.content.text} multiline rows={4} onCommit={(text) => updateContent({ text })} />
            </label>
          )}
        </div>
      </div>
    </div>
  );
}

function ConditionRuleProperties({ rule, fieldGroups, onChange }: {
  rule: ReportConditionalRule;
  fieldGroups: ReportDesignerFieldGroup[];
  onChange: (patch: Partial<ReportConditionalRule>) => void;
}) {
  const type = rule.comparisonType ?? "Text";
  const issue = conditionRuleIssue(rule);
  function changeType(comparisonType: ReportComparisonType) {
    const operator = conditionOperatorOptions(comparisonType).some(option => option.value === rule.operator) ? rule.operator : "Equals";
    onChange({ comparisonType, operator, ignoreCase: comparisonType === "Text" && rule.ignoreCase === true });
  }
  return <>
    <div className="new-report-property-grid">
      <FieldPathInput className="new-report-property-wide" label="条件字段" value={rule.fieldPath} fieldGroups={fieldGroups} selectOnly onChange={fieldPath => onChange({ fieldPath })} />
      <label><span>比较类型</span><select value={type} onChange={event => changeType(event.target.value as ReportComparisonType)}>
        <option value="Text">文本</option><option value="Number">数字</option><option value="Date">日期</option>
      </select></label>
      <label><span>判断</span><select value={rule.operator} onChange={event => onChange({ operator: event.target.value as ReportConditionalRule["operator"] })}>
        {conditionOperatorOptions(type).map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select></label>
      {conditionNeedsValue(rule) ? <label className="new-report-property-wide"><span>比较值</span>
        <CommitTextField value={rule.value} placeholder={type === "Date" ? "YYYY-MM-DD" : type === "Number" ? "例如 1000.50，不含单位或千位分隔符" : "要匹配的文本"} onCommit={value => onChange({ value })} />
      </label> : null}
      {type === "Text" && conditionNeedsValue(rule) ? <DesignerCheckbox checked={rule.ignoreCase === true} onChange={ignoreCase => onChange({ ignoreCase })}>忽略大小写</DesignerCheckbox> : null}
    </div>
    {issue ? <p className="form-error" role="alert">{issue}</p> : null}
  </>;
}
