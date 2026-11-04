import { RecordWorkspace } from "../../ui/layout/Workspaces.tsx";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { PageState, InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import BackupSettingsPanel from "./BackupSettingsPanel.tsx";
import "../../styles/settings-center.css";

export function BackupRecoveryPage({ client, canManageSettings }: { client: ExportDocManagerApiClient; canManageSettings: boolean }) {
  const location = useLocation();
  const [error, setError] = useState<string | null>(null);
  const health = useQuery({ queryKey: queryKeys.health(), queryFn: ({ signal }) => client.getHealth({ signal }), enabled: canManageSettings });
  if (!canManageSettings) return <PageState tone="permission" title="需要系统设置管理权限" />;
  return <RecordWorkspace title="备份与恢复" description="选择备份计划、文件、恢复或迁移任务。">
    {error && <InlineNotice tone="error">{error}</InlineNotice>}
    {health.isError ? <PageState tone="error" title="无法读取运行模式" description={readApiError(health.error)} action={<button type="button" onClick={() => void health.refetch()}>重试</button>} />
      : !health.data ? <PageState tone="loading" title="正在读取运行模式" />
      : <BackupSettingsPanel client={client} databaseProvider={health.data.databaseProviderKey ?? null}
        disabled={false} canManageSettings={canManageSettings} search={location.search} onPathError={setError} />}
  </RecordWorkspace>;
}
