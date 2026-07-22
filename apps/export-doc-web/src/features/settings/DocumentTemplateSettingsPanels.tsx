import { useMemo } from "react";
import { ArrowDown, ArrowUp, FolderOpen, Plus, Trash2 } from "lucide-react";
import { isDesktopBridgeAvailable, selectReportTemplateFile } from "../../desktop/desktopBridge.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

type SettingsRecord = Record<string, unknown>;

export type ReportTemplateOption = {
  templatePath: string;
  displayName?: string;
};

type BatchExportItemDraft = {
  name: string;
  templatePath: string;
  isEnabled: boolean;
  showSeal: boolean;
  reportType: string;
};

type TemplateSettingsPanelProps = {
  settings: SettingsRecord;
  canManageSettings: boolean;
  isBusy: boolean;
  templates: ReportTemplateOption[];
  templatesLoading: boolean;
  templateErrorMessage: string | null;
  onChange: (path: string[], value: unknown) => void;
  onActionError: (error: unknown) => void;
};

export function BatchExportSettingsPanel({
  settings,
  canManageSettings,
  isBusy,
  templates,
  templatesLoading,
  templateErrorMessage,
  onChange,
  onActionError,
}: TemplateSettingsPanelProps) {
  const items = readBatchExportItemsForSettings(settings);
  const templateOptions = useMemo(() => buildTemplateOptions(templates, items), [items, templates]);
  const disabled = !canManageSettings || isBusy;
  const canSelectTemplateFile = isDesktopBridgeAvailable();

  function updateItems(nextItems: BatchExportItemDraft[]) {
    onChange(["batchExport", "items"], nextItems.map(toBatchExportItemRecord));
  }

  function addItem() {
    const firstTemplate = templates[0];
    const templatePath = firstTemplate?.templatePath ?? "";
    updateItems([
      ...items,
      {
        name: firstTemplate?.displayName || fileNameFromPath(templatePath) || "新单证",
        templatePath,
        isEnabled: true,
        showSeal: true,
        reportType: "ExportDocument",
      },
    ]);
  }

  function updateItem(index: number, patch: Partial<BatchExportItemDraft>) {
    updateItems(
      items.map((item, itemIndex) => {
        if (itemIndex !== index) {
          return item;
        }

        const nextItem = { ...item, ...patch };
        if (patch.templatePath !== undefined && !item.name.trim()) {
          nextItem.name = readTemplateDisplayName(patch.templatePath, templates);
        }

        return nextItem;
      }),
    );
  }

  function removeItem(index: number) {
    updateItems(items.filter((_, itemIndex) => itemIndex !== index));
  }

  function moveItem(index: number, offset: number) {
    const targetIndex = index + offset;
    if (targetIndex < 0 || targetIndex >= items.length) {
      return;
    }

    const nextItems = [...items];
    const [item] = nextItems.splice(index, 1);
    nextItems.splice(targetIndex, 0, item);
    updateItems(nextItems);
  }

  async function chooseTemplateFile(index: number) {
    if (disabled) {
      return;
    }

    try {
      const selected = await selectReportTemplateFile();
      if (selected) {
        updateItem(index, { templatePath: selected });
      }
    } catch (error) {
      onActionError(error);
    }
  }

  return (
    <section className="form-section batch-export-settings-section" aria-label="单证模板设置">
      <div className="section-header">
        <h2>单证模板设置</h2>
        <div className="toolbar-actions">
          <button className="command-button secondary" type="button" disabled={disabled} onClick={addItem}>
            <Plus size={17} aria-hidden="true" />
            <span>新增单证</span>
          </button>
        </div>
      </div>
      {templateErrorMessage ? <div className="alert">{templateErrorMessage}</div> : null}
      <fieldset className="settings-fieldset" disabled={!canManageSettings}>
        <div className="field-grid">
          <TextSetting settings={settings} path={["batchExport", "outputFileNamePattern"]} label="文件命名规则" onChange={onChange} />
          <TextSetting settings={settings} path={["batchExport", "outputFolderPattern"]} label="文件夹命名规则" onChange={onChange} />
          <CheckboxSetting settings={settings} path={["batchExport", "mergePdf"]} label="默认合并 PDF" onChange={onChange} />
          <CheckboxSetting settings={settings} path={["batchExport", "zipAfterExport"]} label="默认生成 ZIP" onChange={onChange} />
        </div>
        <div className="batch-export-items-toolbar">
          <span>{items.length} 个导出项</span>
          <span>{templatesLoading ? "模板加载中" : `${templates.length} 个可用模板`}</span>
        </div>
        <ResponsiveTableFrame className="batch-export-items-frame" label="单据包导出项">
          <table className="batch-export-items-table" aria-label="单据包导出项">
            <thead>
              <tr>
                <th>顺序</th>
                <th>启用</th>
                <th>名称</th>
                <th>模板</th>
                <th>模板路径</th>
                <th>带章</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              {items.length > 0 ? (
                items.map((item, index) => (
                  <tr key={`${index}-${item.templatePath || item.name}`}>
                    <td className="batch-export-order-cell">{index + 1}</td>
                    <td>
                      <input
                        className="batch-export-check-input"
                        type="checkbox"
                        checked={item.isEnabled}
                        disabled={disabled}
                        aria-label={`启用 ${item.name || index + 1}`}
                        onChange={(event) => updateItem(index, { isEnabled: event.target.checked })}
                      />
                    </td>
                    <td>
                      <input
                        className="batch-export-cell-input"
                        value={item.name}
                        disabled={disabled}
                        onChange={(event) => updateItem(index, { name: event.target.value })}
                      />
                    </td>
                    <td>
                      <select
                        className="batch-export-cell-input"
                        value={item.templatePath}
                        disabled={disabled || templatesLoading || templateOptions.length === 0}
                        onChange={(event) => updateItem(index, { templatePath: event.target.value })}
                      >
                        {templateOptions.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <div className="batch-export-path-control">
                        <input
                          className="batch-export-cell-input batch-export-path-input"
                          value={item.templatePath}
                          disabled={disabled}
                          onChange={(event) => updateItem(index, { templatePath: event.target.value })}
                        />
                        {canSelectTemplateFile ? (
                          <button
                            className="icon-button compact-icon-button batch-export-path-button"
                            type="button"
                            title="选择模板文件" aria-label="选择模板文件"
                            disabled={disabled}
                            onClick={() => chooseTemplateFile(index)}
                          >
                            <FolderOpen size={15} aria-hidden="true" />
                          </button>
                        ) : null}
                      </div>
                    </td>
                    <td>
                      <input
                        className="batch-export-check-input"
                        type="checkbox"
                        checked={item.showSeal}
                        disabled={disabled}
                        aria-label={`带章 ${item.name || index + 1}`}
                        onChange={(event) => updateItem(index, { showSeal: event.target.checked })}
                      />
                    </td>
                    <td>
                      <div className="batch-export-row-actions">
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="上移" aria-label="上移"
                          disabled={disabled || index === 0}
                          onClick={() => moveItem(index, -1)}
                        >
                          <ArrowUp size={15} aria-hidden="true" />
                        </button>
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="下移" aria-label="下移"
                          disabled={disabled || index >= items.length - 1}
                          onClick={() => moveItem(index, 1)}
                        >
                          <ArrowDown size={15} aria-hidden="true" />
                        </button>
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="删除" aria-label="删除"
                          disabled={disabled}
                          onClick={() => removeItem(index)}
                        >
                          <Trash2 size={15} aria-hidden="true" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td className="empty-cell" colSpan={7}>
                    暂无导出项
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </ResponsiveTableFrame>
      </fieldset>
    </section>
  );
}

export function PaymentTemplateSettingsPanel({
  settings,
  canManageSettings,
  isBusy,
  templates,
  templatesLoading,
  templateErrorMessage,
  onChange,
  onActionError,
}: TemplateSettingsPanelProps) {
  const items = readPaymentTemplateItemsForSettings(settings);
  const templateOptions = useMemo(() => buildTemplateOptions(templates, items), [items, templates]);
  const disabled = !canManageSettings || isBusy;
  const canSelectTemplateFile = isDesktopBridgeAvailable();

  function updateItems(nextItems: BatchExportItemDraft[]) {
    onChange(["paymentTemplates"], nextItems.map(toPaymentTemplateRecord));
  }

  function addItem() {
    const firstTemplate = templates[0];
    const templatePath = firstTemplate?.templatePath ?? "";
    updateItems([
      ...items,
      {
        name: firstTemplate?.displayName || fileNameFromPath(templatePath) || "新付款单模板",
        templatePath,
        isEnabled: true,
        showSeal: true,
        reportType: "PaymentVoucher",
      },
    ]);
  }

  function updateItem(index: number, patch: Partial<BatchExportItemDraft>) {
    updateItems(
      items.map((item, itemIndex) => {
        if (itemIndex !== index) {
          return item;
        }

        const nextItem = { ...item, ...patch };
        if (patch.templatePath !== undefined && !item.name.trim()) {
          nextItem.name = readTemplateDisplayName(patch.templatePath, templates);
        }

        return nextItem;
      }),
    );
  }

  function removeItem(index: number) {
    updateItems(items.filter((_, itemIndex) => itemIndex !== index));
  }

  function moveItem(index: number, offset: number) {
    const targetIndex = index + offset;
    if (targetIndex < 0 || targetIndex >= items.length) {
      return;
    }

    const nextItems = [...items];
    const [item] = nextItems.splice(index, 1);
    nextItems.splice(targetIndex, 0, item);
    updateItems(nextItems);
  }

  async function chooseTemplateFile(index: number) {
    if (disabled) {
      return;
    }

    try {
      const selected = await selectReportTemplateFile();
      if (selected) {
        updateItem(index, { templatePath: selected });
      }
    } catch (error) {
      onActionError(error);
    }
  }

  return (
    <section className="form-section batch-export-settings-section" aria-label="付款/报销模板设置">
      <div className="section-header">
        <h2>付款/报销模板设置</h2>
        <div className="toolbar-actions">
          <button className="command-button secondary" type="button" disabled={disabled} onClick={addItem}>
            <Plus size={17} aria-hidden="true" />
            <span>新增模板</span>
          </button>
        </div>
      </div>
      {templateErrorMessage ? <div className="alert">{templateErrorMessage}</div> : null}
      <fieldset className="settings-fieldset" disabled={!canManageSettings}>
        <div className="batch-export-items-toolbar">
          <span>{items.length} 个付款/报销模板</span>
          <span>{templatesLoading ? "模板加载中" : `${templates.length} 个可用模板`}</span>
        </div>
        <ResponsiveTableFrame className="batch-export-items-frame" label="付款和报销模板">
          <table className="batch-export-items-table" aria-label="付款/报销模板">
            <thead>
              <tr>
                <th>顺序</th>
                <th>启用</th>
                <th>名称</th>
                <th>模板</th>
                <th>模板路径</th>
                <th>带章</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              {items.length > 0 ? (
                items.map((item, index) => (
                  <tr key={`${index}-${item.templatePath || item.name}`}>
                    <td className="batch-export-order-cell">{index + 1}</td>
                    <td>
                      <input
                        className="batch-export-check-input"
                        type="checkbox"
                        checked={item.isEnabled}
                        disabled={disabled}
                        aria-label={`启用付款模板 ${item.name || index + 1}`}
                        onChange={(event) => updateItem(index, { isEnabled: event.target.checked })}
                      />
                    </td>
                    <td>
                      <input
                        className="batch-export-cell-input"
                        value={item.name}
                        disabled={disabled}
                        onChange={(event) => updateItem(index, { name: event.target.value })}
                      />
                    </td>
                    <td>
                      <select
                        className="batch-export-cell-input"
                        value={item.templatePath}
                        disabled={disabled || templatesLoading || templateOptions.length === 0}
                        onChange={(event) => updateItem(index, { templatePath: event.target.value })}
                      >
                        {templateOptions.map((option) => (
                          <option key={option.value} value={option.value}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <div className="batch-export-path-control">
                        <input
                          className="batch-export-cell-input batch-export-path-input"
                          value={item.templatePath}
                          disabled={disabled}
                          onChange={(event) => updateItem(index, { templatePath: event.target.value })}
                        />
                        {canSelectTemplateFile ? (
                          <button
                            className="icon-button compact-icon-button batch-export-path-button"
                            type="button"
                            title="选择模板文件" aria-label="选择模板文件"
                            disabled={disabled}
                            onClick={() => chooseTemplateFile(index)}
                          >
                            <FolderOpen size={15} aria-hidden="true" />
                          </button>
                        ) : null}
                      </div>
                    </td>
                    <td>
                      <input
                        className="batch-export-check-input"
                        type="checkbox"
                        checked={item.showSeal}
                        disabled={disabled}
                        aria-label={`付款模板带章 ${item.name || index + 1}`}
                        onChange={(event) => updateItem(index, { showSeal: event.target.checked })}
                      />
                    </td>
                    <td>
                      <div className="batch-export-row-actions">
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="上移" aria-label="上移"
                          disabled={disabled || index === 0}
                          onClick={() => moveItem(index, -1)}
                        >
                          <ArrowUp size={15} aria-hidden="true" />
                        </button>
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="下移" aria-label="下移"
                          disabled={disabled || index >= items.length - 1}
                          onClick={() => moveItem(index, 1)}
                        >
                          <ArrowDown size={15} aria-hidden="true" />
                        </button>
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="删除" aria-label="删除"
                          disabled={disabled}
                          onClick={() => removeItem(index)}
                        >
                          <Trash2 size={15} aria-hidden="true" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td className="empty-cell" colSpan={7}>
                    暂无付款/报销模板
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </ResponsiveTableFrame>
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
  onChange: (path: string[], value: string) => void;
}) {
  return (
    <label>
      <span>{label}</span>
      <input value={readString(settings, path)} onChange={(event) => onChange(path, event.target.value)} />
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
  onChange: (path: string[], value: boolean) => void;
}) {
  return (
    <label className="settings-check">
      <input type="checkbox" checked={readBoolean(settings, path)} onChange={(event) => onChange(path, event.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

function readBatchExportItemsForSettings(settings: SettingsRecord) {
  const batchExport = readNestedValue(settings, ["batchExport"]);
  if (!isRecord(batchExport)) {
    return [];
  }

  const rawItems = readRecordValue(batchExport, "items", "Items");
  if (!Array.isArray(rawItems)) {
    return [];
  }

  const items: BatchExportItemDraft[] = [];
  for (const rawItem of rawItems) {
    if (!isRecord(rawItem)) {
      continue;
    }

    const reportType = readRecordString(rawItem, "reportType", "ReportType") || "ExportDocument";
    if (!isExportBatchReportType(reportType)) {
      continue;
    }

    items.push({
      name: readRecordString(rawItem, "name", "Name"),
      templatePath: readRecordString(rawItem, "templatePath", "TemplatePath"),
      isEnabled: readRecordBoolean(rawItem, true, "isEnabled", "IsEnabled"),
      showSeal: readRecordBoolean(rawItem, true, "showSeal", "ShowSeal"),
      reportType,
    });
  }

  return items;
}

function readPaymentTemplateItemsForSettings(settings: SettingsRecord) {
  const rawItems = readNestedValue(settings, ["paymentTemplates"]);
  if (!Array.isArray(rawItems)) {
    return [];
  }

  const items: BatchExportItemDraft[] = [];
  for (const rawItem of rawItems) {
    if (!isRecord(rawItem)) {
      continue;
    }

    const reportType = readRecordString(rawItem, "reportType", "ReportType") || "PaymentVoucher";
    if (!isPaymentTemplateReportType(reportType)) {
      continue;
    }

    items.push({
      name: readRecordString(rawItem, "name", "Name"),
      templatePath: readRecordString(rawItem, "templatePath", "TemplatePath"),
      isEnabled: readRecordBoolean(rawItem, true, "isEnabled", "IsEnabled"),
      showSeal: readRecordBoolean(rawItem, true, "showSeal", "ShowSeal"),
      reportType,
    });
  }

  return items;
}

function toBatchExportItemRecord(item: BatchExportItemDraft) {
  return {
    name: item.name.trim() || "新单证",
    templatePath: item.templatePath.trim(),
    isEnabled: item.isEnabled,
    showSeal: item.showSeal,
    reportType: item.reportType.trim() || "ExportDocument",
  };
}

function toPaymentTemplateRecord(item: BatchExportItemDraft) {
  return {
    name: item.name.trim() || "新付款单模板",
    templatePath: item.templatePath.trim(),
    isEnabled: item.isEnabled,
    showSeal: item.showSeal,
    reportType: "PaymentVoucher",
  };
}

function buildTemplateOptions(templates: ReportTemplateOption[], items: BatchExportItemDraft[]) {
  const options: Array<{ value: string; label: string }> = [{ value: "", label: "未选择" }];
  const seen = new Set<string>([""]);

  for (const template of templates) {
    const templatePath = template.templatePath?.trim() ?? "";
    if (!templatePath || seen.has(templatePath.toLowerCase())) {
      continue;
    }

    options.push({
      value: templatePath,
      label: template.displayName || fileNameFromPath(templatePath),
    });
    seen.add(templatePath.toLowerCase());
  }

  for (const item of items) {
    const templatePath = item.templatePath.trim();
    if (!templatePath || seen.has(templatePath.toLowerCase())) {
      continue;
    }

    options.push({
      value: templatePath,
      label: fileNameFromPath(templatePath),
    });
    seen.add(templatePath.toLowerCase());
  }

  return options;
}

function readTemplateDisplayName(templatePath: string, templates: ReportTemplateOption[]) {
  const normalizedTemplatePath = templatePath.trim().toLowerCase();
  const template = templates.find((item) => item.templatePath.trim().toLowerCase() === normalizedTemplatePath);
  return template?.displayName || fileNameFromPath(templatePath) || "新单证";
}

function isExportBatchReportType(reportType: string) {
  const normalized = reportType.trim().toLowerCase();
  return (
    normalized.length === 0 ||
    normalized === "exportdocument" ||
    normalized === "commercialinvoice" ||
    normalized === "packinglist" ||
    normalized === "generic"
  );
}

function isPaymentTemplateReportType(reportType: string) {
  const normalized = reportType.trim().toLowerCase();
  return (
    normalized.length === 0 ||
    normalized === "paymentvoucher" ||
    normalized === "paymentdocument" ||
    normalized === "internal"
  );
}

function readString(settings: SettingsRecord, path: string[]) {
  const value = readNestedValue(settings, path);
  return typeof value === "string" ? value : value == null ? "" : String(value);
}

function readBoolean(settings: SettingsRecord, path: string[]) {
  return readNestedValue(settings, path) === true;
}

function readNestedValue(settings: SettingsRecord, path: string[]) {
  let current: unknown = settings;
  for (const key of path) {
    if (!isRecord(current)) {
      return undefined;
    }

    current = current[key];
  }

  return current;
}

function readRecordValue(record: SettingsRecord, ...keys: string[]) {
  for (const key of keys) {
    if (Object.prototype.hasOwnProperty.call(record, key)) {
      return record[key];
    }
  }

  return undefined;
}

function readRecordString(record: SettingsRecord, ...keys: string[]) {
  const value = readRecordValue(record, ...keys);
  return typeof value === "string" ? value.trim() : value == null ? "" : String(value).trim();
}

function readRecordBoolean(record: SettingsRecord, fallback: boolean, ...keys: string[]) {
  const value = readRecordValue(record, ...keys);
  return typeof value === "boolean" ? value : fallback;
}

function fileNameFromPath(path: string) {
  return path.split(/[\\/]/).filter(Boolean).pop() || path;
}

function isRecord(value: unknown): value is SettingsRecord {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
