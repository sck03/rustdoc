import { RecordWorkspace } from "../../ui/layout/Workspaces.tsx";
import { Link } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState, InlineNotice, ConcurrencyConflictNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useSettingsWorkspace } from "./useSettingsWorkspace.ts";
import MaintenanceSettingsPanels from "./MaintenanceSettingsPanels.tsx";
import { LogMaintenanceSettingsPanel } from "./LogMaintenanceSettingsPanel.tsx";
import "../../styles/runtime-diagnostics.css";
import "../../styles/settings-center.css";

export function SystemMaintenancePage(props: {
  client: ExportDocManagerApiClient; canManageSettings: boolean; canManageUsers: boolean;
  canUseDocumentWorkspace: boolean; isDesktopRuntime: boolean; productName: string;
}) {
  const model = useSettingsWorkspace({ ...props, operationMode: "maintenance" });
  if (!props.canManageSettings) return <PageState tone="permission" title="需要系统设置管理权限" />;
  return <RecordWorkspace title="系统维护" description="检查运行状态，管理日志、数据归属和后台任务。">
    {model.message && <InlineNotice tone="error">{model.message}</InlineNotice>}
    {model.successMessage && <InlineNotice tone="success">{model.successMessage}</InlineNotice>}
    {model.concurrencyMessage && <ConcurrencyConflictNotice message={model.concurrencyMessage} isBusy={model.isBusy} onReload={() => void model.handleReloadSettings()} />}
    <MaintenanceSettingsPanels {...props} health={model.healthQuery.data ?? null} healthIsBusy={model.healthQuery.isFetching}
      healthErrorMessage={model.healthQuery.isError ? readApiError(model.healthQuery.error) : null}
      onRefreshHealth={() => void model.healthQuery.refetch()} onPathError={model.setMessage}
      logPanel={model.settings ? <>
        <LogMaintenanceSettingsPanel settings={model.settings} disabled={model.isBusy}
          hasUnsavedChanges={model.hasUnsavedChanges} onChange={model.patchSetting} onCleanup={() => void model.handleCleanupSystemLogs()} />
        <button type="button" className="command-button" disabled={model.isBusy || !model.hasUnsavedChanges} onClick={model.save}>保存日志留存规则</button>
      </> : <PageState tone="loading" title="正在读取日志留存规则" />}
      jobsPanel={<section className="form-section" aria-label="后台任务状态"><h2>后台任务状态</h2>
        <p>导入、导出、报表和备份在后台办理，任务结果和失败记录可在文件任务中查看。</p>
        <Link className="command-button secondary" to="/jobs">查看文件任务</Link>
      </section>} />
  </RecordWorkspace>;
}
