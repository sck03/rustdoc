import { Plus } from "lucide-react";
import {
  ApiUserReportTemplateDto,
  ApiUserReportTemplateVersionDto,
} from "../../api/index.ts";
import { SelectField, TextField } from "../../ui/FormFields.tsx";

const reportTemplateShareScopeOptions = [
  { value: "Private", label: "仅自己可见" },
  { value: "Department", label: "同部门可见" },
  { value: "Company", label: "同公司可见" },
  { value: "All", label: "团队成员可见" },
];

export function reportTemplateShareScopeLabel(value?: string) {
  return reportTemplateShareScopeOptions.find((item) => item.value === value)?.label ?? "仅自己可见";
}

export function ReportTemplateUserPanel({
  currentTemplate,
  versions,
  versionsLoading,
  newTemplateName,
  newTemplateShareScope,
  isBusy,
  canCreate,
  isUserTemplate,
  onNewTemplateNameChange,
  onNewTemplateShareScopeChange,
  onCreate,
  onShareScopeChange,
  onToggleActive,
  onRestoreVersion,
}: {
  currentTemplate: ApiUserReportTemplateDto | null;
  versions: ApiUserReportTemplateVersionDto[];
  versionsLoading: boolean;
  newTemplateName: string;
  newTemplateShareScope: string;
  isBusy: boolean;
  canCreate: boolean;
  isUserTemplate: boolean;
  onNewTemplateNameChange: (value: string) => void;
  onNewTemplateShareScopeChange: (value: string) => void;
  onCreate: () => void;
  onShareScopeChange: (value: string) => void;
  onToggleActive: () => void;
  onRestoreVersion: (versionNumber: number) => void;
}) {
  return (
    <details className="template-management-panel template-actions-panel template-user-panel" open aria-label="我的模板">
      <summary>
        <span>我的模板</span>
        <small>默认私有，可明确共享</small>
      </summary>
      <div className="template-management-content">
        <section className="template-management-section" aria-label="复制为我的模板">
          <div className="template-management-section-title">
            <strong>{currentTemplate?.canEdit ? "复制当前模板" : "从当前模板创建"}</strong>
          </div>
          <TextField label="新模板名称" value={newTemplateName} disabled={isBusy} onChange={onNewTemplateNameChange} />
          <SelectField
            label="共享范围"
            value={newTemplateShareScope}
            disabled={isBusy}
            options={reportTemplateShareScopeOptions}
            onChange={onNewTemplateShareScopeChange}
          />
          <button className="command-button secondary" type="button" disabled={!canCreate} onClick={onCreate}>
            <Plus size={17} aria-hidden="true" />
            <span>{isUserTemplate ? "复制为我的模板" : "参考当前默认模板创建"}</span>
          </button>
        </section>

        {currentTemplate ? (
          <section className="template-management-section template-current-template-section" aria-label="当前用户模板">
            <div className="template-management-section-title">
              <strong>{currentTemplate.canEdit ? "当前为我的模板" : "当前为他人共享模板"}</strong>
            </div>
            <div className="template-status-chips" aria-label="模板状态">
              <span className={`template-status-chip ${currentTemplate.isActive ? "active" : "inactive"}`}>
                {currentTemplate.isActive ? "已启用" : "已停用"}
              </span>
              <span className={`template-status-chip ${currentTemplate.isShared ? "shared" : "private"}`}>
                {reportTemplateShareScopeLabel(currentTemplate.shareScope)}
              </span>
              <span className="template-status-chip version">V{currentTemplate.versionNumber}</span>
            </div>
            <small>
              {currentTemplate.canEdit
                ? currentTemplate.isShared
                  ? "符合共享范围的团队成员可查看和复制，只有你可以修改或删除。"
                  : "仅你自己可见和使用。"
                : "共享模板只读；复制后可自行修改。"}
            </small>
            {currentTemplate.canEdit ? (
              <div className="template-management-actions template-publish-actions">
                <SelectField
                  label="共享范围"
                  value={currentTemplate.shareScope}
                  disabled={isBusy}
                  options={reportTemplateShareScopeOptions}
                  onChange={onShareScopeChange}
                />
                <button
                  className={`command-button compact-button ${currentTemplate.isActive ? "danger-button" : "secondary"}`}
                  type="button"
                  disabled={isBusy}
                  onClick={onToggleActive}
                >
                  {currentTemplate.isActive ? "停用模板" : "重新启用"}
                </button>
              </div>
            ) : null}
            <details className="template-inline-details">
              <summary>版本历史 ({versions.length})</summary>
              <div className="template-version-list">
                {versionsLoading ? <small>正在读取历史版本…</small> : null}
                {!versionsLoading && versions.length === 0 ? <small>保存后会在这里保留可恢复快照。</small> : null}
                {versions.map((version) => (
                  <div className="template-version-row" key={version.id}>
                    <div>
                      <strong>V{version.versionNumber} · {version.changeType}</strong>
                      <small>
                        {version.changedBy || "当前用户"} · {new Date(version.createdAt).toLocaleString()}
                      </small>
                    </div>
                    {currentTemplate.canEdit && version.canRestore && version.versionNumber !== currentTemplate.versionNumber ? (
                      <button
                        className="command-button secondary compact-button"
                        type="button"
                        disabled={isBusy}
                        onClick={() => onRestoreVersion(version.versionNumber)}
                      >
                        恢复
                      </button>
                    ) : null}
                  </div>
                ))}
              </div>
            </details>
          </section>
        ) : null}
      </div>
    </details>
  );
}
