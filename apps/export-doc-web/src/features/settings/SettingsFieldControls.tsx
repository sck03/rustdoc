import { FolderOpen } from "lucide-react";
import { NumberField, SelectField } from "../../ui/FormFields.tsx";
import { readBoolean, readNestedValue, readNumberValue } from "./settingsValueUtils.ts";
import type { SettingsRecord } from "./settingsTypes.ts";

export function TextSetting({ settings, path, label, disabled, placeholder, list, className, onChange }: {
  settings: SettingsRecord;
  path: string[];
  label: string;
  disabled?: boolean;
  placeholder?: string;
  list?: string;
  className?: string;
  onChange: (path: string[], value: string) => void;
}) {
  return (
    <label className={className}>
      <span>{label}</span>
      <input value={readSettingString(settings, path)} disabled={disabled} placeholder={placeholder} list={list} onChange={(event) => onChange(path, event.target.value)} />
    </label>
  );
}

export function DirectorySetting({ settings, path, label, disabled, canSelectDirectory, onChange, onSelectDirectory }: {
  settings: SettingsRecord;
  path: string[];
  label: string;
  disabled?: boolean;
  canSelectDirectory: boolean;
  onChange: (path: string[], value: string) => void;
  onSelectDirectory: () => void;
}) {
  return (
    <label>
      <span>{label}</span>
      <div className="settings-path-control">
        <input className="settings-path-input" value={readSettingString(settings, path)} disabled={disabled} onChange={(event) => onChange(path, event.target.value)} />
        {canSelectDirectory ? (
          <button className="icon-button compact-icon-button settings-path-button" type="button" title="选择默认导出目录" disabled={disabled} onClick={onSelectDirectory}>
            <FolderOpen size={15} aria-hidden="true" />
          </button>
        ) : null}
      </div>
    </label>
  );
}

export function TextAreaSetting({ settings, path, label, disabled, placeholder, className, onChange }: {
  settings: SettingsRecord;
  path: string[];
  label: string;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
  onChange: (path: string[], value: string) => void;
}) {
  return (
    <label className={className ? `textarea-field settings-textarea-field ${className}` : "textarea-field settings-textarea-field"}>
      <span>{label}</span>
      <textarea value={readSettingString(settings, path)} disabled={disabled} placeholder={placeholder} onChange={(event) => onChange(path, event.target.value)} />
    </label>
  );
}

export function NumberSetting({ settings, path, label, disabled, onChange }: {
  settings: SettingsRecord;
  path: string[];
  label: string;
  disabled?: boolean;
  onChange: (path: string[], value: number) => void;
}) {
  return <NumberField label={label} value={readNumberValue(settings, path)} disabled={disabled} step="1" onChange={(value) => onChange(path, value)} />;
}

export function SelectSetting({ settings, path, label, disabled, options, onChange }: {
  settings: SettingsRecord;
  path: string[];
  label: string;
  disabled?: boolean;
  options: Array<{ value: string; label: string }>;
  onChange: (path: string[], value: string) => void;
}) {
  return <SelectField label={label} value={readSettingString(settings, path)} disabled={disabled} options={options} onChange={(value) => onChange(path, value)} />;
}

export function CheckboxSetting({ settings, path, label, disabled, className, onChange }: {
  settings: SettingsRecord;
  path: string[];
  label: string;
  disabled?: boolean;
  className?: string;
  onChange: (path: string[], value: boolean) => void;
}) {
  return (
    <label className={className ? `settings-check ${className}` : "settings-check"}>
      <input type="checkbox" checked={readBoolean(settings, path)} disabled={disabled} onChange={(event) => onChange(path, event.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

export function SecretToggle({ checked, disabled, onChange }: { checked: boolean; disabled?: boolean; onChange: (value: boolean) => void }) {
  return (
    <label className="inline-check">
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} />
      <span>更新敏感字段</span>
    </label>
  );
}

export function readSettingString(settings: SettingsRecord, path: string[]) {
  const value = readNestedValue(settings, path);
  return typeof value === "string" ? value : value == null ? "" : String(value);
}
