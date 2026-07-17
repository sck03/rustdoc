import type { ApiSettingsSecretsDto } from "../../api/index.ts";
import { CheckboxSetting, DirectorySetting, NumberSetting, SelectSetting, TextSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";

export function RuntimeDatabaseSettingsPanel({ settings, secrets, canManageSettings, updateSecrets, isBusy, canSelectDesktopDirectory, onChange, onSelectDefaultExportDirectory }: {
  settings: SettingsRecord;
  secrets: ApiSettingsSecretsDto | null;
  canManageSettings: boolean;
  updateSecrets: boolean;
  isBusy: boolean;
  canSelectDesktopDirectory: boolean;
  onChange: (path: string[], value: unknown) => void;
  onSelectDefaultExportDirectory: () => void;
}) {
  return (
    <>
      <section className="form-section" aria-label="系统与数据库">
        <div className="section-header"><h2>系统与数据库</h2></div>
        <fieldset className="settings-fieldset" disabled={!canManageSettings}>
          <div className="field-grid">
            <TextSetting settings={settings} path={["system", "appName"]} label="软件名称" onChange={onChange} />
            <SelectSetting settings={settings} path={["system", "databaseProvider"]} label="数据库类型" options={[{ value: "Sqlite", label: "SQLite" }, { value: "PostgreSQL", label: "PostgreSQL" }]} onChange={onChange} />
            <TextSetting settings={settings} path={["system", "sqliteDatabaseFileName"]} label="SQLite 文件名" onChange={onChange} />
            <DirectorySetting settings={settings} path={["system", "defaultExportDirectory"]} label="默认导出目录" disabled={isBusy || !canManageSettings} canSelectDirectory={canSelectDesktopDirectory} onChange={onChange} onSelectDirectory={onSelectDefaultExportDirectory} />
            <NumberSetting settings={settings} path={["system", "itemEntryBlankRowCount"]} label="明细空白行数" onChange={onChange} />
            <NumberSetting settings={settings} path={["system", "backupRetentionDays"]} label="备份保留天数" onChange={onChange} />
            <NumberSetting settings={settings} path={["system", "auditLogRetentionDays"]} label="审计保留天数" onChange={onChange} />
            <NumberSetting settings={settings} path={["system", "logRetentionDays"]} label="日志保留天数" onChange={onChange} />
            <NumberSetting settings={settings} path={["system", "logRetainedFileCount"]} label="日志保留文件数" onChange={onChange} />
            <NumberSetting settings={settings} path={["system", "logFileSizeLimitMB"]} label="单日志大小 MB" onChange={onChange} />
            <TextSetting settings={settings} path={["system", "defaultTemplateExporterNameCn"]} label="默认出口商中文名" placeholder="Excel 未提供中文名时使用" onChange={onChange} />
          </div>
        </fieldset>
      </section>
      <section className="form-section" aria-label="PostgreSQL">
        <div className="section-header"><h2>PostgreSQL</h2></div>
        <fieldset className="settings-fieldset" disabled={!canManageSettings}>
          <div className="field-grid">
            <TextSetting settings={settings} path={["system", "postgreSqlHost"]} label="服务器" onChange={onChange} />
            <NumberSetting settings={settings} path={["system", "postgreSqlPort"]} label="端口" onChange={onChange} />
            <TextSetting settings={settings} path={["system", "postgreSqlDatabase"]} label="数据库名" onChange={onChange} />
            <TextSetting settings={settings} path={["system", "postgreSqlUsername"]} label="账号" onChange={onChange} />
            <TextSetting disabled={!updateSecrets} placeholder={secrets?.postgreSqlPasswordSet ? "已保存，勾选后可更新" : ""} settings={settings} path={["system", "postgreSqlPassword"]} label="密码" onChange={onChange} />
            <TextSetting settings={settings} path={["system", "postgreSqlAdditionalOptions"]} label="附加参数" onChange={onChange} />
            <CheckboxSetting settings={settings} path={["system", "postgreSqlAutoBackupEnabled"]} label="启用自动物理备份" onChange={onChange} />
            <SelectSetting settings={settings} path={["system", "postgreSqlAutoBackupSchedule"]} label="自动备份周期" options={[{ value: "Daily", label: "每天" }, { value: "Weekly", label: "每周" }]} onChange={onChange} />
            <TextSetting settings={settings} path={["system", "postgreSqlAutoBackupTime"]} label="自动备份时间" placeholder="02:00" onChange={onChange} />
            <SelectSetting settings={settings} path={["system", "postgreSqlAutoBackupDayOfWeek"]} label="每周备份星期" options={[{ value: "1", label: "星期一" }, { value: "2", label: "星期二" }, { value: "3", label: "星期三" }, { value: "4", label: "星期四" }, { value: "5", label: "星期五" }, { value: "6", label: "星期六" }, { value: "0", label: "星期日" }]} onChange={(path, value) => onChange(path, Number(value))} />
            <NumberSetting settings={settings} path={["system", "postgreSqlAutoBackupRetentionCount"]} label="PostgreSQL 保留份数" onChange={onChange} />
          </div>
        </fieldset>
      </section>
    </>
  );
}

export default RuntimeDatabaseSettingsPanel;
