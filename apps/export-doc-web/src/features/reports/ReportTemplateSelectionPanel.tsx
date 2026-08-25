import { ApiReportTemplateDto, ApiUserReportTemplateDto } from "../../api/index.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { fileNameFromPath, matchesTemplatePath, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";
import { CircleCheckBig } from "lucide-react";

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
  const selectedTemplateIsDefault = selectedUserTemplateId <= 0 &&
    matchesTemplatePath(selectedTemplatePath, defaultTemplatePath);

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
      <div className="template-default-selection">
        <SelectField
          label="文件模板"
          className="template-select-field"
          value={selectedUserTemplateId > 0 ? "" : selectedTemplatePath}
          disabled={isBusy || templates.length === 0}
          options={templates.map((template) => ({
            value: template.templatePath,
            label: `${matchesTemplatePath(template.templatePath, defaultTemplatePath) ? "默认 · " : ""}${template.displayName || fileNameFromPath(template.templatePath)}`,
          }))}
          onChange={onTemplateChange}
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
      <SelectField
        label="我的 / 共享模板"
        className="template-select-field"
        value={selectedUserTemplateId > 0 ? String(selectedUserTemplateId) : ""}
        disabled={isBusy}
        options={[
          { value: "", label: "选择用户模板" },
          ...userTemplates.map((template) => ({
            value: String(template.id),
            label: `${template.canEdit ? "我的" : "共享"} · ${template.name}`,
          })),
        ]}
        onChange={onUserTemplateChange}
      />
    </div>
  );
}
