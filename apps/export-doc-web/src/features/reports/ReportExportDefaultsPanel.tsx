import { Save } from "lucide-react";
import type { AppSettings } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { TextField } from "../../ui/FormFields.tsx";

export function ReportExportDefaultsPanel({
  settings,
  canManageSettings,
  isBusy,
  isDirty,
  onChange,
  onSave,
}: {
  settings: AppSettings | null;
  canManageSettings: boolean;
  isBusy: boolean;
  isDirty: boolean;
  onChange: (path: string[], value: unknown) => void;
  onSave: () => void;
}) {
  return (
    <section className="template-management-panel report-export-defaults-panel" aria-label="导出默认设置">
      <div className="report-export-defaults-header">
        <div>
          <strong>导出默认设置</strong>
          <small>文件命名、目录命名以及批量导出的默认选项</small>
        </div>
        <button
          className="command-button secondary compact-button"
          type="button"
          disabled={!canManageSettings || !isDirty || isBusy}
          onClick={onSave}
        >
          <Save size={16} aria-hidden="true" />
          <span>保存设置</span>
        </button>
      </div>
      <div className="template-management-content">
        {settings ? (
          <fieldset className="settings-fieldset" disabled={!canManageSettings || isBusy}>
            <div className="report-export-defaults-grid">
              <TextField
                label="文件命名规则"
                value={settings.batchExport.outputFileNamePattern}
                onChange={(value) => onChange(["batchExport", "outputFileNamePattern"], value)}
              />
              <TextField
                label="文件夹命名规则"
                value={settings.batchExport.outputFolderPattern}
                onChange={(value) => onChange(["batchExport", "outputFolderPattern"], value)}
              />
              <CheckboxSetting
                label="默认合并 PDF"
                checked={settings.batchExport.mergePdf}
                onChange={(value) => onChange(["batchExport", "mergePdf"], value)}
              />
              <CheckboxSetting
                label="默认生成 ZIP"
                checked={settings.batchExport.zipAfterExport}
                onChange={(value) => onChange(["batchExport", "zipAfterExport"], value)}
              />
            </div>
          </fieldset>
        ) : (
          <PageState tone="loading" title="正在加载导出默认设置" />
        )}
      </div>
    </section>
  );
}

function CheckboxSetting({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="settings-check">
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      <span>{label}</span>
    </label>
  );
}
