import { lazy, Suspense, useEffect, useState } from "react";
import { Cloud } from "lucide-react";
import type { ApiSettingsSecretsDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { CheckboxSetting, NumberSetting, SelectSetting, TextSetting, readSettingString } from "./SettingsFieldControls.tsx";
import { SettingsSectionNav } from "./SettingsSectionNav.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";

const BackupManagementPanel = lazy(() => import("./BackupManagementPanel.tsx"));
const PostgreSqlMaintenancePanel = lazy(() => import("./MaintenancePostgreSqlPanel.tsx").then((module) => ({ default: module.PostgreSqlMaintenancePanel })));
type BackupSection = "policy" | "webdav" | "data" | "postgresql";

function readSection(search: string): BackupSection {
  const section = new URLSearchParams(search).get("section");
  return section === "webDav" ? "webdav" : section === "postgresql" ? "postgresql" : section === "backup" ? "data" : "policy";
}

export default function BackupSettingsPanel({ client, settings, secrets, databaseProvider, disabled, canManageSettings, updateSecrets, search, onChange, onTestWebDavConnection, onPathError }: {
  client: ExportDocManagerApiClient;
  settings: SettingsRecord;
  secrets: ApiSettingsSecretsDto | null;
  databaseProvider: string | null;
  disabled: boolean;
  canManageSettings: boolean;
  updateSecrets: boolean;
  search: string;
  onChange: (path: string[], value: unknown) => void;
  onTestWebDavConnection: () => void;
  onPathError: (message: string) => void;
}) {
  const [section, setSection] = useState(() => readSection(search));
  useEffect(() => setSection(readSection(search)), [search]);
  const isPostgreSql = databaseProvider === "PostgreSQL";
  const activeSection = section === "postgresql" && !isPostgreSql ? "policy" : section;
  const sections: { key: BackupSection; label: string }[] = [
    { key: "policy", label: "计划与保留" }, { key: "webdav", label: "WebDAV 云备份" },
    { key: "data", label: "数据备份与还原" },
    ...(isPostgreSql ? [{ key: "postgresql" as const, label: "团队库与完整迁移" }] : []),
  ];
  return <>
    <SettingsSectionNav label="备份与恢复分类" sections={sections} activeSection={activeSection} onSelect={setSection} />
    {activeSection === "policy" && <section className="form-section" aria-label="备份计划与保留">
      <div className="section-header"><h2>备份计划与保留</h2></div>
      <p className="form-field-description">数据备份保留天数为 0 时不自动清理。修改计划后，请点击“保存全部修改”。</p>
      <fieldset className="settings-fieldset" disabled={disabled}>
        <div className="field-grid">
          <NumberSetting settings={settings} path={["system", "backupRetentionDays"]} label="数据备份保留天数" onChange={onChange} />
          {isPostgreSql && <>
            <CheckboxSetting settings={settings} path={["system", "postgreSqlAutoBackupEnabled"]} label="启用自动物理备份" onChange={onChange} />
            <SelectSetting settings={settings} path={["system", "postgreSqlAutoBackupSchedule"]} label="自动备份周期" options={[{ value: "Daily", label: "每天" }, { value: "Weekly", label: "每周" }]} onChange={onChange} />
            <TextSetting settings={settings} path={["system", "postgreSqlAutoBackupTime"]} label="自动备份时间" placeholder="02:00" onChange={onChange} />
            {readSettingString(settings, ["system", "postgreSqlAutoBackupSchedule"]) === "Weekly" && <SelectSetting settings={settings} path={["system", "postgreSqlAutoBackupDayOfWeek"]} label="每周备份星期" options={["日", "一", "二", "三", "四", "五", "六"].map((day, index) => ({ value: String(index), label: `星期${day}` }))} onChange={(path, value) => onChange(path, Number(value))} />}
            <NumberSetting settings={settings} path={["system", "postgreSqlAutoBackupRetentionCount"]} label="PostgreSQL 保留份数" onChange={onChange} />
          </>}
        </div>
      </fieldset>
    </section>}
    {activeSection === "webdav" && <section className="form-section" aria-label="WebDAV 云备份">
      <div className="section-header"><h2>WebDAV 云备份</h2>
        <button className="command-button secondary" type="button" disabled={disabled} onClick={onTestWebDavConnection}>
          <Cloud size={17} aria-hidden="true" /><span>测试 WebDAV</span>
        </button>
      </div>
      <p className="form-field-description">测试使用已保存的连接配置。上传和下载在“数据备份与还原”中操作。</p>
      <fieldset className="settings-fieldset" disabled={disabled}>
        <div className="field-grid">
          <TextSetting settings={settings} path={["webDav", "url"]} label="WebDAV 地址" onChange={onChange} />
          <TextSetting settings={settings} path={["webDav", "userName"]} label="WebDAV 用户" onChange={onChange} />
          <TextSetting disabled={!updateSecrets} placeholder={secrets?.webDavPasswordSet ? "已保存，勾选后可更新" : ""} settings={settings} path={["webDav", "password"]} label="WebDAV 密码" onChange={onChange} />
          <CheckboxSetting settings={settings} path={["webDav", "enabled"]} label="启用 WebDAV 备份" onChange={onChange} />
        </div>
      </fieldset>
    </section>}
    <Suspense fallback={<PageState tone="loading" title="正在加载备份工具" />}>
      {activeSection === "data" && <BackupManagementPanel client={client} canManageSettings={canManageSettings && !disabled} onPathError={onPathError} />}
      {activeSection === "postgresql" && <PostgreSqlMaintenancePanel client={client} canManageSettings={canManageSettings && !disabled} onPathError={onPathError} />}
    </Suspense>
  </>;
}
