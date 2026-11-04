import { lazy, Suspense, useEffect } from "react";
import { RefreshCw, Save } from "lucide-react";
import type { ApiBackupPlanStatus, ExportDocManagerApiClient } from "../../api/index.ts";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { SegmentedNav } from "../../ui/primitives/Tabs.tsx";
import { backupSections, operationSection, type BackupSection } from "./systemOperationNavigation.ts";
import { BackupSchedulePanel } from "./BackupSchedulePanel.tsx";
import { BackupReplicaPanel } from "./BackupReplicaPanel.tsx";
import { BackupArchivesPanel } from "./BackupArchivesPanel.tsx";
import { BackupArchiveRestorePanel } from "./BackupArchiveRestorePanel.tsx";
import { useBackupPlan } from "./useBackupPlan.ts";
import { backupResultLabel } from "./backupPlanModel.ts";
import { formatRuntimeDate } from "./settingsFormatters.ts";

const BackupManagementPanel = lazy(() => import("./BackupManagementPanel.tsx"));
const PostgreSqlMaintenancePanel = lazy(() => import("./MaintenancePostgreSqlPanel.tsx").then((module) => ({ default: module.PostgreSqlMaintenancePanel })));

export default function BackupSettingsPanel({ client, databaseProvider, disabled, canManageSettings, search, onPathError, onSettingsSaved, onDirtyChange }: {
  client: ExportDocManagerApiClient; databaseProvider: string | null; disabled: boolean; canManageSettings: boolean;
  search: string; onPathError: (message: string) => void; onSettingsSaved?: (status: ApiBackupPlanStatus, previousRevision: number) => void; onDirtyChange?: (dirty: boolean) => void;
}) {
  const c = useBackupPlan(client, canManageSettings, onSettingsSaved);
  const { update } = useRouteQuery();
  const section = operationSection(search, "backup") as BackupSection;
  const postgres = databaseProvider === "postgresql";
  const selectedId = new URLSearchParams(search).get("archive") ?? "";
  const setSection = (section: BackupSection, archive: string | null = null) => update({ section, archive }, false);
  useEffect(() => { onDirtyChange?.(c.dirty); }, [c.dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange?.(false), [onDirtyChange]);
  const busy = disabled || c.busy;
  if (!canManageSettings) return <PageState tone="error" title="只有系统管理员可以管理备份与恢复" />;
  return <>
    <SegmentedNav label="备份与恢复分类" items={backupSections} value={section} onChange={setSection} />
    {c.query.isError && <InlineNotice tone="error" title="无法读取灾备配置">{readApiError(c.query.error)}</InlineNotice>}
    {c.message && <InlineNotice tone="error">{c.message}</InlineNotice>}
    {c.success && <InlineNotice tone="success">{c.success}</InlineNotice>}
    <div className="section-header backup-plan-toolbar"><span role="status">{c.dirty ? "灾备设置有未保存修改" : "灾备设置已保存"}</span><div className="toolbar-actions">
      <button className="command-button secondary" type="button" disabled={c.busy || c.query.isFetching} onClick={() => void c.reload()}><RefreshCw size={16} aria-hidden="true" /><span>重新加载灾备设置</span></button>
      {(section === "plan" || section === "replica") && <button className="command-button" type="button" disabled={busy || !c.draft || !c.dirty} onClick={c.save}><Save size={16} aria-hidden="true" /><span>{section === "plan" ? "保存计划" : "保存副本设置"}</span></button>}
    </div></div>
    {!c.status && c.query.isLoading && <PageState tone="loading" title="正在读取灾备设置" />}
    {section === "plan" && c.status && <InlineNotice tone={c.status.lastError ? "warning" : "info"} title={`最近灾备：${backupResultLabel(c.status.lastResult)}`}>
      {formatRuntimeDate(c.status.lastRunAt ?? undefined)}{c.status.lastError && ` · ${c.status.lastError}`}
    </InlineNotice>}
    {c.jobId && <InlineNotice tone={c.job?.status === "Failed" ? "error" : "info"} title={c.job?.title || "后台任务"}
      action={<div className="toolbar-actions"><ViewJobButton jobId={c.jobId} />{c.activeJob && <button className="command-button secondary" type="button" disabled={c.busy} onClick={c.cancel}>取消任务</button>}</div>}>
      {c.job?.errorMessage || c.job?.detailText || c.job?.statusText || "正在读取任务状态"}
    </InlineNotice>}
    {section === "plan" && <BackupSchedulePanel controller={c} disabled={busy} />}
    {section === "archives" && <BackupArchivesPanel controller={c} disabled={busy} onRestore={(id) => setSection("recovery", id)} />}
    {section === "replica" && <BackupReplicaPanel controller={c} disabled={busy} />}
    {section === "recovery" && databaseProvider && <BackupArchiveRestorePanel client={client} controller={c} disabled={busy} postgres={postgres} selectedId={selectedId} />}
    <Suspense fallback={<PageState tone="loading" title="正在加载迁移与恢复工具" />}>
      {(section === "migration" || section === "recovery") && databaseProvider && (postgres
        ? <PostgreSqlMaintenancePanel client={client} canManageSettings={!busy} onPathError={onPathError} view={section} />
        : <BackupManagementPanel client={client} canManageSettings={!busy} onPathError={onPathError} view={section} />)}
    </Suspense>
  </>;
}
