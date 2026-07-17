import type { ApiSettingsSecretsDto, ApiSingleWindowIssuingAuthorityOptionDto } from "../../api/index.ts";
import { TextAreaSetting, TextSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";
import { singleWindowCustomsCooAplAddPath, singleWindowCustomsCooFetchPlacePath, singleWindowCustomsCooOrgCodePath } from "./settingsConfigurationPaths.ts";

export function SingleWindowSettingsPanel({ settings, secrets, issuingAuthorityOptions, canManageSettings, updateSecrets, onChange, onOrgCodeChange, onFetchPlaceChange, onAplAddChange }: {
  settings: SettingsRecord;
  secrets: ApiSettingsSecretsDto | null;
  issuingAuthorityOptions: ApiSingleWindowIssuingAuthorityOptionDto[];
  canManageSettings: boolean;
  updateSecrets: boolean;
  onChange: (path: string[], value: unknown) => void;
  onOrgCodeChange: (value: string) => void;
  onFetchPlaceChange: (value: string) => void;
  onAplAddChange: (value: string) => void;
}) {
  return (
    <div className="settings-single-window-stack" aria-label="AI 与单一窗口">
      <section className="form-section settings-ai-section" aria-label="AI 设置">
        <div className="section-header"><h2>AI 设置</h2></div>
        <fieldset className="settings-fieldset" disabled={!canManageSettings}>
          <div className="field-grid settings-ai-grid">
            <TextSetting settings={settings} path={["ai", "apiEndpoint"]} label="AI API 地址" onChange={onChange} />
            <TextSetting settings={settings} path={["ai", "modelName"]} label="AI 模型" onChange={onChange} />
            <TextSetting disabled={!updateSecrets} placeholder={secrets?.aiApiKeySet ? "已保存，勾选后可更新" : ""} settings={settings} path={["ai", "apiKey"]} label="AI API Key" onChange={onChange} />
            <TextAreaSetting settings={settings} path={["ai", "systemPrompt"]} label="AI 系统提示词" onChange={onChange} />
          </div>
        </fieldset>
      </section>
      <section className="form-section settings-single-window-defaults-section" aria-label="单一窗口默认值">
        <div className="section-header"><h2>单一窗口默认值</h2></div>
        <fieldset className="settings-fieldset" disabled={!canManageSettings}>
          <div className="field-grid settings-single-window-grid">
            <TextSetting settings={settings} path={["singleWindow", "customsCooDefaults", "applName"]} label="申报员姓名" onChange={onChange} />
            <TextSetting settings={settings} path={["singleWindow", "customsCooDefaults", "applicant"]} label="申报员身份证号" onChange={onChange} />
            <TextSetting settings={settings} path={["singleWindow", "customsCooDefaults", "applTel"]} label="申报员电话" onChange={onChange} />
            <TextSetting settings={settings} path={singleWindowCustomsCooOrgCodePath} label="签证机构代码(4位)" list="customs-coo-issuing-authority-options" onChange={(_, value) => onOrgCodeChange(value)} />
            <TextSetting settings={settings} path={singleWindowCustomsCooFetchPlacePath} label="领证机构代码(4位)" list="customs-coo-issuing-authority-options" onChange={(_, value) => onFetchPlaceChange(value)} />
            <TextSetting settings={settings} path={singleWindowCustomsCooAplAddPath} label="申请地址(机构所在地)" onChange={(_, value) => onAplAddChange(value)} />
            <datalist id="customs-coo-issuing-authority-options">
              {issuingAuthorityOptions.map((option) => <option key={option.code || option.label} value={option.code} label={option.label} />)}
            </datalist>
          </div>
        </fieldset>
      </section>
    </div>
  );
}

export default SingleWindowSettingsPanel;
