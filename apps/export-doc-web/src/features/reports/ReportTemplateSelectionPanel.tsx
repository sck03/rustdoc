import { ApiReportTemplateDto, ApiUserReportTemplateDto } from "../../api/index.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { fileNameFromPath, matchesTemplatePath, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";
import { CircleCheckBig } from "lucide-react";
import { useState } from "react";

export function ReportTemplateSelectionPanel({
  reportType,
  reportTypeOptions,
  templates,
  userTemplates,
  selectedTemplatePath,
  selectedUserTemplateId,
  defaultTemplatePath,
  isBusy,
  canSetDefault,
  onReportTypeChange,
  onTemplateChange,
  onUserTemplateChange,
  onSetDefault,
}: {
  reportType: ReportTypeOption;
  reportTypeOptions: Array<{ value: ReportTypeOption; label: string }>;
  templates: ApiReportTemplateDto[];
  userTemplates: ApiUserReportTemplateDto[];
  selectedTemplatePath: string;
  selectedUserTemplateId: number;
  defaultTemplatePath: string;
  isBusy: boolean;
  canSetDefault: boolean;
  onReportTypeChange: (value: string) => void;
  onTemplateChange: (value: string) => void;
  onUserTemplateChange: (value: string) => void;
  onSetDefault: () => void;
}) {
  const [search, setSearch] = useState("");
  const selectedValue = selectedUserTemplateId > 0 ? `user-template:${selectedUserTemplateId}` : selectedTemplatePath;
  const selectedTemplateIsDefault = matchesTemplatePath(selectedValue, defaultTemplatePath);
  const fileTemplates = templates.filter((template) => !template.templatePath.startsWith("user-template:"));
  const statusLabels: Record<string, string> = { Draft: "草稿", Published: "已发布", Disabled: "已停用", Archived: "已归档" };
  const entries = [
    ...fileTemplates.map((template) => ({ value: template.templatePath, label: `${template.displayName || fileNameFromPath(template.templatePath)} · 文件模板` })),
    ...userTemplates.map((template) => ({ value: `user-template:${template.id}`, label: `${template.name} · ${template.shareScope === "Private" ? "个人模板" : "共享模板"} · ${statusLabels[template.status] ?? template.status}` })),
  ].map((item) => ({ ...item, label: `${matchesTemplatePath(item.value, defaultTemplatePath) ? "默认 · " : ""}${item.label}` }));
  const options = entries.filter((item) => item.value === selectedValue || item.label.normalize("NFKC").toLowerCase().includes(search.normalize("NFKC").trim().toLowerCase()));

  return (
    <div className="template-selection-panel">
      <SelectField
        label="类型"
        className="template-type-field"
        value={reportType}
        disabled={isBusy}
        options={reportTypeOptions}
        onChange={onReportTypeChange}
      />
      <label className="template-directory-search">查找模板<input type="search" aria-label="搜索模板目录" placeholder="名称、来源或状态" value={search} maxLength={100} onChange={(event) => setSearch(event.target.value)} /></label>
      <div className="template-default-selection">
        <SelectField
          label="模板目录"
          className="template-select-field"
          value={selectedValue}
          disabled={isBusy || options.length === 0}
          options={options}
          onChange={(value) => value.startsWith("user-template:") ? onUserTemplateChange(value.slice("user-template:".length)) : onTemplateChange(value)}
        />
        <button
          className="command-button secondary compact-button"
          type="button"
          disabled={!canSetDefault || selectedTemplateIsDefault}
          onClick={onSetDefault}
        >
          <CircleCheckBig size={16} aria-hidden="true" />
          <span>{selectedTemplateIsDefault ? "当前默认" : "设为默认"}</span>
        </button>
      </div>
    </div>
  );
}
