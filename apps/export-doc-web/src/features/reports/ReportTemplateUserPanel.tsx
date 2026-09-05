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
  isBusy,
  allowCreateBlank,
  allowClone,
  canCreateBlank,
  canClone,
  onNewTemplateNameChange,
  onCreateBlank,
  onClone,
  onShareScopeChange,
  onPublish,
  onDisable,
  onRestore,
  onArchive,
  onRestoreVersion,
}: {
  currentTemplate: ApiUserReportTemplateDto | null;
  versions: ApiUserReportTemplateVersionDto[];
  versionsLoading: boolean;
  newTemplateName: string;
  isBusy: boolean;
  allowCreateBlank: boolean;
  allowClone: boolean;
  canCreateBlank: boolean;
  canClone: boolean;
  onNewTemplateNameChange: (value: string) => void;
  onCreateBlank: () => void;
  onClone: () => void;
  onShareScopeChange: (value: string) => void;
  onPublish: () => void;
  onDisable: () => void;
  onRestore: () => void;
  onArchive: () => void;
  onRestoreVersion: (versionNumber: number) => void;
}) {
  return (
    <details className="template-management-panel template-actions-panel template-user-panel" aria-label="我的和共享模板">
      <summary>
        <span>我的 / 共享模板</span>
        <small>默认私有，可明确共享</small>
      </summary>
      <div className="template-management-content">
        <section className="template-management-section" aria-label="创建我的模板">
          <div className="template-management-section-title">
            <strong>创建我的模板</strong>
          </div>
          <TextField label="新模板名称" value={newTemplateName} disabled={isBusy} onChange={onNewTemplateNameChange} />
          <small>新建和复制都会生成私有草稿；复制内容由服务端从当前模板读取，不接收客户端回传正文。</small>
          <div className="template-management-actions">
            {allowCreateBlank ? (
              <button className="command-button secondary" type="button" disabled={!canCreateBlank} onClick={onCreateBlank}>
                <Plus size={17} aria-hidden="true" />
                <span>新建空白模板</span>
              </button>
            ) : null}
            {allowClone ? (
              <button className="command-button secondary" type="button" disabled={!canClone} onClick={onClone}>
                <Plus size={17} aria-hidden="true" />
                <span>复制当前模板</span>
              </button>
            ) : null}
          </div>
        </section>

        {currentTemplate ? (
          <section className="template-management-section template-current-template-section" aria-label="当前用户模板">
            <div className="template-management-section-title">
              <strong>{currentTemplate.canEdit ? "当前为我的模板" : "当前为他人共享模板"}</strong>
            </div>
            <div className="template-status-chips" aria-label="模板状态">
              <span className={`template-status-chip ${currentTemplate.status === "Published" ? "active" : "inactive"}`}>
                {reportTemplateStatusLabel(currentTemplate.status)}
              </span>
              <span className={`template-status-chip ${currentTemplate.shareScope !== "Private" ? "shared" : "private"}`}>
                {reportTemplateShareScopeLabel(currentTemplate.shareScope)}
              </span>
              <span className="template-status-chip version">V{currentTemplate.versionNumber}</span>
            </div>
            <small>
              {currentTemplate.canEdit
                ? currentTemplate.shareScope !== "Private"
                  ? "符合共享范围的团队成员可查看和复制，只有你可以修改或删除。"
                  : "当前内容仅你自己可见；正式输出需要先发布。"
                : "共享模板只读；复制后可自行修改。"}
            </small>
            {currentTemplate.canPublish || currentTemplate.canShare || currentTemplate.canDisable ||
            currentTemplate.canRestore || currentTemplate.canArchive ? (
              <div className="template-management-actions template-publish-actions">
                {currentTemplate.canShare ? (
                  <SelectField
                    label="共享范围"
                    value={currentTemplate.shareScope}
                    disabled={isBusy}
                    options={reportTemplateShareScopeOptions}
                    onChange={onShareScopeChange}
                  />
                ) : null}
                {currentTemplate.canPublish ? (
                  <button className="command-button compact-button primary" type="button" disabled={isBusy} onClick={onPublish}>
                    发布模板
                  </button>
                ) : null}
                {currentTemplate.canDisable ? (
                  <button className="command-button compact-button danger-button" type="button" disabled={isBusy} onClick={onDisable}>
                    停用模板
                  </button>
                ) : null}
                {currentTemplate.canRestore ? (
                  <button className="command-button compact-button secondary" type="button" disabled={isBusy} onClick={onRestore}>
                    {currentTemplate.status === "Archived" ? "恢复为草稿" : "恢复发布"}
                  </button>
                ) : null}
                {currentTemplate.canArchive ? (
                  <button className="command-button compact-button danger-button" type="button" disabled={isBusy} onClick={onArchive}>
                    归档模板
                  </button>
                ) : null}
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

function reportTemplateStatusLabel(value?: string) {
  switch (value) {
    case "Draft": return "草稿";
    case "Published": return "已发布";
    case "Disabled": return "已停用";
    case "Archived": return "已归档";
    default: return "状态异常";
  }
}
