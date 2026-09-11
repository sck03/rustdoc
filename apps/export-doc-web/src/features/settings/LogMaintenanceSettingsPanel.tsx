import { Trash2 } from "lucide-react";
import { NumberSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";

export function LogMaintenanceSettingsPanel({ settings, disabled, hasUnsavedChanges, onChange, onCleanup }: {
  settings: SettingsRecord;
  disabled: boolean;
  hasUnsavedChanges: boolean;
  onChange: (path: string[], value: unknown) => void;
  onCleanup: () => void;
}) {
  return <section className="form-section" aria-label="日志管理">
    <div className="section-header"><h2>日志管理</h2></div>
    <fieldset className="settings-fieldset" disabled={disabled}>
      <div className="field-grid">
        <NumberSetting settings={settings} path={["system", "auditLogRetentionDays"]} label="审计保留天数" onChange={onChange} />
        <NumberSetting settings={settings} path={["system", "logRetentionDays"]} label="日志保留天数" onChange={onChange} />
        <NumberSetting settings={settings} path={["system", "logRetainedFileCount"]} label="日志保留文件数" onChange={onChange} />
        <NumberSetting settings={settings} path={["system", "logFileSizeLimitMB"]} label="单日志大小 MB" onChange={onChange} />
      </div>
    </fieldset>
    <p className="form-field-description">清理按已保存的日志保留规则执行；审计记录的查询与清理在“系统管理 → 审计日志”中维护。</p>
    {hasUnsavedChanges && <p className="form-field-description">请先保存全部修改，再清理旧日志。</p>}
    <div className="toolbar-actions">
      <button className="command-button secondary" type="button" disabled={disabled || hasUnsavedChanges} onClick={onCleanup}>
        <Trash2 size={17} aria-hidden="true" /><span>清理旧日志</span>
      </button>
    </div>
  </section>;
}
