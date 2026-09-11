import type { ReactNode } from "react";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { DocumentFieldLabelsSettingsPanel } from "./DocumentFieldLabelsSettingsPanel.tsx";
import { NumberSetting, readSettingString } from "./SettingsFieldControls.tsx";
import { SettingsSectionNav } from "./SettingsSectionNav.tsx";
import { readDocumentFieldGroup } from "./settingsNavigationModel.ts";
import type { SettingsRecord } from "./settingsTypes.ts";

const sections = [{ key: "documentDefaults", label: "录入默认值" }, { key: "documentFields", label: "字段名称" }, { key: "singleWindow", label: "申报默认值" }] as const;

export default function DocumentSettingsPanel({ settings, disabled, search, onChange, declarationDefaults }: {
  settings: SettingsRecord;
  disabled: boolean;
  search: string;
  onChange: (path: string[], value: unknown) => void;
  declarationDefaults: ReactNode;
}) {
  const { update } = useRouteQuery();
  const section = sections.find((item) => item.key === new URLSearchParams(search).get("section"))?.key ?? "documentDefaults";
  const group = readDocumentFieldGroup(search);

  return <>
    <p className="form-field-description">连接同一服务的账号共用这些设置。切换分类保留草稿，点击“保存全部修改”后生效。</p>
    <SettingsSectionNav label="单据设置分类" sections={sections} activeSection={section} onSelect={(next) => update({ section: next }, false)} />
    {section === "singleWindow" ? declarationDefaults : section === "documentFields"
      ? <DocumentFieldLabelsSettingsPanel settings={settings} disabled={disabled} group={group} onGroupChange={(next) => update({ group: next })} onChange={onChange} />
      : <section className="form-section" aria-label="发票录入默认值">
        <div className="section-header"><h2>发票录入默认值</h2></div>
        <fieldset className="settings-fieldset" disabled={disabled}>
          <div className="field-grid document-settings-fields">
            <SelectField label="默认显示备用列数"
              value={readSettingString(settings, ["system", "itemEntrySpareColumnCount"]) || "0"}
              includeEmptyOption={false}
              options={Array.from({ length: 11 }, (_, count) => ({ value: String(count), label: count ? `显示前 ${count} 个备用列` : "隐藏全部空备用列（默认）" }))}
              description="新建和编辑发票均适用；已有内容的备用列自动显示。可在明细表的“显示列”中临时调整。"
              onChange={(value) => onChange(["system", "itemEntrySpareColumnCount"], Number(value))} />
            <NumberSetting settings={settings} path={["system", "itemEntryBlankRowCount"]} label="明细空白行数" onChange={onChange} />
          </div>
        </fieldset>
      </section>}
  </>;
}
