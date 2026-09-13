import { ApiReportTemplateDto, UserReportTemplateSummaryRecord } from "../../api/index.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { fileNameFromPath, matchesTemplatePath, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";
import { CircleCheckBig } from "lucide-react";

export function ReportTemplateSelectionPanel({
  reportType,
  reportTypeOptions,
  templates,
  userTemplates,
  directory,
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
  userTemplates: UserReportTemplateSummaryRecord[];
  directory: {
    search: string; pageNumber: number; totalPages: number; totalCount: number; loading: boolean;
    onSearchChange: (search: string) => void; onPageChange: (pageNumber: number) => void;
  };
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
  const search = directory.search;
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
      <label className="template-directory-search">查找模板<input type="search" aria-label="搜索模板目录" placeholder="按模板名称查找" value={search} maxLength={100} onChange={(event) => directory.onSearchChange(event.target.value)} /></label>
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
      {directory.totalPages > 1 ? <div className="template-management-actions" aria-label="用户模板分页">
        <button className="command-button secondary compact-button" type="button" disabled={directory.pageNumber <= 1 || directory.loading} onClick={() => directory.onPageChange(directory.pageNumber - 1)}>上一页</button>
        <small>用户模板 {directory.pageNumber} / {directory.totalPages} 页 · 共 {directory.totalCount} 个</small>
        <button className="command-button secondary compact-button" type="button" disabled={directory.pageNumber >= directory.totalPages || directory.loading} onClick={() => directory.onPageChange(directory.pageNumber + 1)}>下一页</button>
      </div> : null}
    </div>
  );
}
