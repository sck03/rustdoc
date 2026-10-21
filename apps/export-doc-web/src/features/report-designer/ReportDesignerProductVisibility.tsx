import type { ReportDesignerV3Element } from "./reportDesignerV3Schema.ts";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import { DesignerCheckbox } from "./ReportDesignerPropertyControls.tsx";
import { ConditionRuleProperties } from "./ReportDesignerConditionalProperties.tsx";

export function ReportDesignerProductVisibility({ element, fieldGroups, editable, onPatch, onAddSpacer }: {
  element: ReportDesignerV3Element; fieldGroups: ReportDesignerFieldGroup[]; editable: boolean;
  onPatch: (patch: Partial<ReportDesignerV3Element>) => void; onAddSpacer: () => void;
}) {
  const settings = element.product ?? {};
  const fields = fieldGroups.map(group => ({ ...group, fields:group.fields.filter(field => !["doc_seal_path", "customs_seal_path"].includes(field.value)) })).filter(group => group.fields.length);
  return <details className="report-designer-property-section"><summary>显隐与空行</summary><fieldset disabled={!editable}>
    {element.type === "Field" && <DesignerCheckbox checked={settings.hideWhenEmpty === true} onChange={hideWhenEmpty => onPatch({ product:{...settings,hideWhenEmpty} })}>空内容时隐藏字段</DesignerCheckbox>}
    <DesignerCheckbox checked={Boolean(settings.condition)} onChange={enabled => onPatch({ product:{...settings, condition:enabled ? {fieldPath:element.type === "Field" ? element.fieldPath : "item.StyleNo", operator:"HasValue", value:""} : undefined} })}>按条件显示</DesignerCheckbox>
    {settings.condition && <ConditionRuleProperties rule={settings.condition} itemContext fieldGroups={fields} onChange={patch => onPatch({product:{...settings,condition:{...settings.condition!,...patch}}})} />}
    <DesignerCheckbox checked={settings.collapseEmpty === true} onChange={collapseEmpty => onPatch({product:{...settings,collapseEmpty}})}>隐藏时收起空内容行</DesignerCheckbox>
    <button type="button" className="command-button secondary" onClick={onAddSpacer}>在商品末尾添加占位行</button>
  </fieldset></details>;
}
