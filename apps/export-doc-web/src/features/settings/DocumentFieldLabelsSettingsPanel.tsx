import { documentSpareKeys } from "../../ui/documentSpareFields.ts";
import { TextSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { documentFieldGroups, type DocumentFieldGroup } from "./settingsNavigationModel.ts";

export function DocumentFieldLabelsSettingsPanel({ settings, disabled, group, onGroupChange, onChange }: {
  settings: SettingsRecord;
  disabled: boolean;
  group: DocumentFieldGroup;
  onGroupChange: (group: DocumentFieldGroup) => void;
  onChange: (path: string[], value: unknown) => void;
}) {
  return <section className="form-section" aria-label="单据字段名称">
    <div className="section-header"><h2>单据字段名称</h2></div>
    <p className="form-field-description">每组 10 项，名称最多 40 字且不能重复；留空使用默认名称。已有模板的文字标题请在设计器中调整。</p>
    <SelectField label="字段分组" value={group} includeEmptyOption={false} options={[...documentFieldGroups]}
      description="切换分组保留已输入的名称。" onChange={(value) => onGroupChange(value as DocumentFieldGroup)} />
    <fieldset className="settings-fieldset" disabled={disabled}>
      <div className="field-grid document-settings-fields">{documentSpareKeys.map((key, index) => <TextSetting key={`${group}-${key}`}
        settings={settings} path={["system", "documentFieldLabels", group, key]}
        label={`备用 ${index + 1}`} placeholder={index === 0 ? "例如：船名航次" : `备用 ${index + 1}`}
        maxLength={40} onChange={onChange} />)}</div>
    </fieldset>
  </section>;
}
