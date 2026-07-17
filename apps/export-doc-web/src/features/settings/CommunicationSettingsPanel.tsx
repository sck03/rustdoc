import { lazy, Suspense } from "react";
import { Cloud, MailCheck, Sparkles } from "lucide-react";
import type { ApiSettingsSecretsDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { CheckboxSetting, NumberSetting, TextAreaSetting, TextSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";

const LazyBackupManagementPanel = lazy(() => import("./BackupManagementPanel.tsx"));

export function CommunicationSettingsPanel({ client, settings, secrets, canManageSettings, updateSecrets, isBusy, emailAddressCandidate, onChange, onInferEmailServerConfig, onTestEmailConnection, onTestWebDavConnection, onPathError }: {
  client: ExportDocManagerApiClient;
  settings: SettingsRecord;
  secrets: ApiSettingsSecretsDto | null;
  canManageSettings: boolean;
  updateSecrets: boolean;
  isBusy: boolean;
  emailAddressCandidate: string;
  onChange: (path: string[], value: unknown) => void;
  onInferEmailServerConfig: () => void;
  onTestEmailConnection: () => void;
  onTestWebDavConnection: () => void;
  onPathError: (message: string) => void;
}) {
  return (
    <>
      <section className="form-section" aria-label="邮件与备份">
        <div className="section-header">
          <h2>邮件与备份</h2>
          <div className="toolbar-actions">
            <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings || !emailAddressCandidate} onClick={onInferEmailServerConfig} title="根据邮箱地址推断 SMTP 配置">
              <Sparkles size={17} aria-hidden="true" /><span>推断 SMTP</span>
            </button>
            <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings} onClick={onTestEmailConnection}>
              <MailCheck size={17} aria-hidden="true" /><span>测试邮件连接</span>
            </button>
            <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings} onClick={onTestWebDavConnection}>
              <Cloud size={17} aria-hidden="true" /><span>测试 WebDAV</span>
            </button>
          </div>
        </div>
        <fieldset className="settings-fieldset" disabled={!canManageSettings}>
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
            <TextSetting settings={settings} path={["webDav", "url"]} label="WebDAV 地址" onChange={onChange} />
            <TextSetting settings={settings} path={["webDav", "userName"]} label="WebDAV 用户" onChange={onChange} />
            <TextSetting className="webdav-password-setting" disabled={!updateSecrets} placeholder={secrets?.webDavPasswordSet ? "已保存，勾选后可更新" : ""} settings={settings} path={["webDav", "password"]} label="WebDAV 密码" onChange={onChange} />
            <CheckboxSetting className="webdav-enabled-setting" settings={settings} path={["webDav", "enabled"]} label="启用 WebDAV 备份" onChange={onChange} />
          </div>
        </fieldset>
      </section>
      <Suspense fallback={<div className="loading-panel">正在加载备份工具</div>}>
        <LazyBackupManagementPanel client={client} canManageSettings={canManageSettings} onPathError={onPathError} />
      </Suspense>
    </>
  );
}

export default CommunicationSettingsPanel;
