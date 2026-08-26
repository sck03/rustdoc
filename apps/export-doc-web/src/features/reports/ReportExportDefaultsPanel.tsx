import { ArrowDown, ArrowUp, Plus, Save, Trash2 } from "lucide-react";
import type { ApiReportTemplateDto, AppSettings, BatchExportItem } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { TextField } from "../../ui/FormFields.tsx";
import { createBatchExportItem, resolveBatchExportItems } from "./reportExportDefaultsModel.ts";

export function ReportExportDefaultsPanel({
  settings,
  canManageSettings,
  isBusy,
  isDirty,
  onChange,
  onSave,
  templates,
}: {
  settings: AppSettings | null;
  canManageSettings: boolean;
  isBusy: boolean;
  isDirty: boolean;
  onChange: (path: string[], value: unknown) => void;
  onSave: () => void;
  templates: ApiReportTemplateDto[];
}) {
  const items = settings ? resolveBatchExportItems(settings.batchExport.items, templates) : [];

  return (
    <section className="template-management-panel report-export-defaults-panel" aria-label="发票单据包默认设置">
      <div className="report-export-defaults-header">
        <div>
          <strong>发票单据包默认设置</strong>
          <small>文件命名、目录命名以及发票单据包的默认输出方式</small>
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
            <BatchExportItems
              items={items}
              templates={templates}
              mergePdf={settings.batchExport.mergePdf}
              zipAfterExport={settings.batchExport.zipAfterExport}
              onChange={(items) => onChange(["batchExport", "items"], items)}
            />
          </fieldset>
        ) : (
          <PageState tone="loading" title="正在加载导出默认设置" />
        )}
      </div>
    </section>
  );
}

function BatchExportItems({
  items,
  templates,
  mergePdf,
  zipAfterExport,
  onChange,
}: {
  items: BatchExportItem[];
  templates: ApiReportTemplateDto[];
  mergePdf: boolean;
  zipAfterExport: boolean;
  onChange: (items: BatchExportItem[]) => void;
}) {
  const update = (index: number, patch: Partial<BatchExportItem>) =>
    onChange(items.map((item, itemIndex) => itemIndex === index ? { ...item, ...patch } : item));
  const move = (index: number, offset: -1 | 1) => {
    const target = index + offset;
    if (target < 0 || target >= items.length) return;
    const next = [...items];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };
  const add = () => {
    const used = new Set(items.map((item) => item.templatePath));
    const template = templates.find((item) => !used.has(item.templatePath));
    if (!template) return;
    onChange([...items, createBatchExportItem(template)]);
  };

  const enabledCount = items.filter((item) => item.isEnabled).length;
  const summary = `${items.length} 个单据 · ${enabledCount} 个启用 · ${mergePdf ? "合并 PDF" : "不合并 PDF"} · ${zipAfterExport ? "生成 ZIP" : "不生成 ZIP"}`;

  return (
    <details className="report-export-default-items" aria-label="发票单据项默认配置">
      <summary>
        <span className="report-export-default-items-summary">
          <strong>发票单据项</strong>
          <small>{summary}</small>
        </span>
      </summary>
      <div className="report-export-default-items-content">
        <div className="report-export-default-items-header">
          <small>维护顺序、启用状态、盖章和显示名称</small>
          <button className="command-button secondary compact-button" type="button" disabled={!templates.some((item) => !items.some((current) => current.templatePath === item.templatePath))} onClick={add}>
            <Plus size={16} aria-hidden="true" /><span>添加单据模板</span>
          </button>
        </div>
        <div className="report-export-default-items-list">
          {items.length === 0 ? <small className="report-export-default-items-empty">暂无可用发票模板</small> : items.map((item, index) => (
            <div className="report-export-default-item" key={`${index}-${item.templatePath}`}>
              <input type="checkbox" checked={item.isEnabled} aria-label={`启用 ${item.name || index + 1}`} onChange={(event) => update(index, { isEnabled: event.target.checked })} />
              <input value={item.name} aria-label={`单据名称 ${index + 1}`} onChange={(event) => update(index, { name: event.target.value })} />
              <select value={item.templatePath} aria-label={`单据模板 ${index + 1}`} onChange={(event) => update(index, { templatePath: event.target.value })}>
                {templates.map((template) => <option key={template.templatePath} value={template.templatePath}>{template.displayName}</option>)}
              </select>
              <label className="settings-check"><input type="checkbox" checked={item.showSeal} onChange={(event) => update(index, { showSeal: event.target.checked })} /><span>带章</span></label>
              <button className="icon-button compact-icon-button" type="button" title="上移" aria-label="上移" disabled={index === 0} onClick={() => move(index, -1)}><ArrowUp size={15} aria-hidden="true" /></button>
              <button className="icon-button compact-icon-button" type="button" title="下移" aria-label="下移" disabled={index === items.length - 1} onClick={() => move(index, 1)}><ArrowDown size={15} aria-hidden="true" /></button>
              <button className="icon-button compact-icon-button" type="button" title="删除" aria-label="删除" disabled={items.length <= 1} onClick={() => onChange(items.filter((_, itemIndex) => itemIndex !== index))}><Trash2 size={15} aria-hidden="true" /></button>
            </div>
          ))}
        </div>
      </div>
    </details>
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
