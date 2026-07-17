import { ApiReportTemplateDto, ApiUserReportTemplateDto } from "../../api/index.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { fileNameFromPath, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function ReportTemplateSelectionPanel({
  reportType,
  reportTypeOptions,
  templates,
  userTemplates,
  selectedTemplatePath,
  selectedUserTemplateId,
  isBusy,
  onReportTypeChange,
  onTemplateChange,
  onUserTemplateChange,
}: {
  reportType: ReportTypeOption;
  reportTypeOptions: Array<{ value: ReportTypeOption; label: string }>;
  templates: ApiReportTemplateDto[];
  userTemplates: ApiUserReportTemplateDto[];
  selectedTemplatePath: string;
  selectedUserTemplateId: number;
  isBusy: boolean;
  onReportTypeChange: (value: string) => void;
  onTemplateChange: (value: string) => void;
  onUserTemplateChange: (value: string) => void;
}) {
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
      <SelectField
        label="默认模板"
        className="template-select-field"
        value={selectedUserTemplateId > 0 ? "" : selectedTemplatePath}
        disabled={isBusy || templates.length === 0}
        options={templates.map((template) => ({
          value: template.templatePath,
          label: template.displayName || fileNameFromPath(template.templatePath),
        }))}
        onChange={onTemplateChange}
      />
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
