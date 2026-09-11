import type { ApiSettingsSecretsDto } from "../../api/index.ts";
import { TextAreaSetting, TextSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";

export default function AiSettingsPanel({ settings, secrets, disabled, updateSecrets, onChange }: {
  settings: SettingsRecord;
  secrets: ApiSettingsSecretsDto | null;
  disabled: boolean;
  updateSecrets: boolean;
  onChange: (path: string[], value: unknown) => void;
}) {
  return <section className="form-section settings-ai-section" aria-label="AI 服务">
    <div className="section-header"><h2>AI 服务</h2></div>
    <fieldset className="settings-fieldset" disabled={disabled}>
      <div className="field-grid settings-ai-grid">
        <TextSetting settings={settings} path={["ai", "apiEndpoint"]} label="AI API 地址" onChange={onChange} />
        <TextSetting settings={settings} path={["ai", "modelName"]} label="AI 模型" onChange={onChange} />
        <TextSetting disabled={!updateSecrets} placeholder={secrets?.aiApiKeySet ? "已保存，勾选后可更新" : ""} settings={settings} path={["ai", "apiKey"]} label="AI API Key" onChange={onChange} />
        <TextAreaSetting settings={settings} path={["ai", "systemPrompt"]} label="AI 系统提示词" onChange={onChange} />
      </div>
    </fieldset>
  </section>;
}
