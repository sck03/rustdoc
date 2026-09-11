import { MailCheck, Sparkles } from "lucide-react";
import type { ApiSettingsSecretsDto } from "../../api/index.ts";
import { CheckboxSetting, NumberSetting, TextAreaSetting, TextSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";
export function CommunicationSettingsPanel({ settings, secrets, canManageSettings, updateSecrets, isBusy, emailAddressCandidate, onChange, onInferEmailServerConfig, onTestEmailConnection }: {
  settings: SettingsRecord;
  secrets: ApiSettingsSecretsDto | null;
  canManageSettings: boolean;
  updateSecrets: boolean;
  isBusy: boolean;
  emailAddressCandidate: string;
  onChange: (path: string[], value: unknown) => void;
  onInferEmailServerConfig: () => void;
  onTestEmailConnection: () => void;
}) {
  return (
    <>
      <section className="form-section" aria-label="邮件设置">
        <div className="section-header">
          <h2>邮件设置</h2>
          <div className="toolbar-actions">
            <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings || !emailAddressCandidate} onClick={onInferEmailServerConfig} title="根据邮箱地址推断 SMTP 配置">
              <Sparkles size={17} aria-hidden="true" /><span>推断 SMTP</span>
            </button>
            <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings} onClick={onTestEmailConnection}>
              <MailCheck size={17} aria-hidden="true" /><span>测试邮件连接</span>
            </button>
          </div>
        </div>
        <fieldset className="settings-fieldset" disabled={!canManageSettings || isBusy}>
          <div className="field-grid communication-settings-grid">
            <TextSetting settings={settings} path={["email", "smtpHost"]} label="SMTP 服务器" onChange={onChange} />
            <NumberSetting settings={settings} path={["email", "smtpPort"]} label="SMTP 端口" onChange={onChange} />
            <TextSetting settings={settings} path={["email", "userName"]} label="邮箱账号" onChange={onChange} />
            <TextSetting disabled={!updateSecrets} placeholder={secrets?.emailPasswordSet ? "已保存，勾选后可更新" : ""} settings={settings} path={["email", "password"]} label="邮箱密码" onChange={onChange} />
            <CheckboxSetting settings={settings} path={["email", "enableSsl"]} label="启用 SSL" onChange={onChange} />
            <TextSetting settings={settings} path={["email", "fromAddress"]} label="发件人地址" onChange={onChange} />
            <TextSetting settings={settings} path={["email", "fromDisplayName"]} label="发件人名称" onChange={onChange} />
            <TextSetting settings={settings} path={["email", "documentEmailSubjectTemplate"]} label="单据邮件主题" onChange={onChange} />
            <TextAreaSetting className="email-body-template-field" settings={settings} path={["email", "documentEmailBodyTemplate"]} label="单据邮件正文" onChange={onChange} />
            <TextAreaSetting settings={settings} path={["email", "recipientAllowList"]} label="收件人白名单（每行邮箱或域名，留空不限制）" onChange={onChange} />
            <TextAreaSetting settings={settings} path={["email", "recipientBlockList"]} label="收件人黑名单（每行邮箱或域名，优先拒绝）" onChange={onChange} />
          </div>
        </fieldset>
      </section>
    </>
  );
}

export default CommunicationSettingsPanel;
