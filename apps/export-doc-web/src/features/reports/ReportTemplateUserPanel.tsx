import { Plus } from "lucide-react";
import { useState } from "react";
import {
  ApiUserReportTemplateDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { SelectField, TextField } from "../../ui/FormFields.tsx";
import { ReportTemplateVersionHistory } from "./ReportTemplateVersionHistory.tsx";

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
  client,
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
  client: ExportDocManagerApiClient;
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
  const [expanded, setExpanded] = useState(false);
  return (
    <details className="template-management-panel template-actions-panel template-user-panel" aria-label="我的和共享模板" open={expanded} onToggle={event => setExpanded(event.currentTarget.open)}>
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
            <ReportTemplateVersionHistory key={currentTemplate.id} client={client} template={currentTemplate} enabled={expanded} isBusy={isBusy} onRestore={onRestoreVersion} />
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
