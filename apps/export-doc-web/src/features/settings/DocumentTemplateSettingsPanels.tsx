import type { ChangeEvent } from "react";

type SettingsRecord = Record<string, unknown>;

type ExportDefaultsProps = {
  settings: SettingsRecord;
  canManageSettings: boolean;
  isBusy: boolean;
  onChange: (path: string[], value: unknown) => void;
};

export function BatchExportSettingsPanel({
  settings,
  canManageSettings,
  isBusy,
  onChange,
}: ExportDefaultsProps) {
  return (
    <section className="form-section batch-export-settings-section" aria-label="导出默认设置">
      <div className="section-header">
        <h2>导出默认设置</h2>
      </div>
      <fieldset className="settings-fieldset" disabled={!canManageSettings || isBusy}>
        <div className="field-grid">
          <TextSetting
            settings={settings}
            path={["batchExport", "outputFileNamePattern"]}
            label="文件命名规则"
            onChange={onChange}
          />
          <TextSetting
            settings={settings}
            path={["batchExport", "outputFolderPattern"]}
            label="文件夹命名规则"
            onChange={onChange}
          />
          <CheckboxSetting
            settings={settings}
            path={["batchExport", "mergePdf"]}
            label="默认合并 PDF"
            onChange={onChange}
          />
          <CheckboxSetting
            settings={settings}
            path={["batchExport", "zipAfterExport"]}
            label="默认生成 ZIP"
            onChange={onChange}
          />
        </div>
      </fieldset>
    </section>
  );
}

function TextSetting({
  settings,
  path,
  label,
  onChange,
}: {
  settings: SettingsRecord;
  path: string[];
  label: string;
  onChange: (path: string[], value: unknown) => void;
}) {
  return (
    <label>
      <span>{label}</span>
      <input
        value={readString(settings, path)}
        onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(path, event.target.value)}
      />
    </label>
  );
}

function CheckboxSetting({
  settings,
  path,
  label,
  onChange,
}: {
  settings: SettingsRecord;
  path: string[];
  label: string;
  onChange: (path: string[], value: unknown) => void;
}) {
  return (
    <label className="settings-check">
      <input
        type="checkbox"
        checked={readNestedValue(settings, path) === true}
        onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(path, event.target.checked)}
      />
      <span>{label}</span>
    </label>
  );
}

function readString(settings: SettingsRecord, path: string[]) {
  const value = readNestedValue(settings, path);
  return typeof value === "string" ? value : value == null ? "" : String(value);
}

function readNestedValue(settings: SettingsRecord, path: string[]) {
  let current: unknown = settings;
  for (const key of path) {
    if (!current || typeof current !== "object" || Array.isArray(current)) {
      return undefined;
    }
    current = (current as SettingsRecord)[key];
  }
  return current;
}
