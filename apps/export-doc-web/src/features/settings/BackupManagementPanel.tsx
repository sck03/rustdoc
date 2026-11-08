import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { DataRootMigrationPanel } from "./DataRootMigrationPanel.tsx";
import { DatabaseBackupPanel } from "./DatabaseBackupPanel.tsx";
import { DatabaseBackupTable } from "./DatabaseBackupTable.tsx";
import { DisasterRecoveryPanel } from "./DisasterRecoveryPanel.tsx";
import { useBackupManagement } from "./useBackupManagement.ts";

export default function BackupManagementPanel({
  client,
  canManageSettings,
  onPathError,
  view = "recovery",
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  onPathError: (message: string) => void;
  view?: "migration" | "recovery";
}) {
  const controller = useBackupManagement(client, canManageSettings, { database: view === "recovery" });

  return (
    <section className="form-section backup-management-section" aria-label="数据备份与还原">
      {view === "recovery" && <DatabaseBackupPanel controller={controller} onPathError={onPathError} />}
      {controller.message ? <InlineNotice tone="error" title="备份操作失败">{controller.message}</InlineNotice> : null}
      {controller.successMessage ? (
        <InlineNotice
          tone="success"
          action={controller.lastCreatedJobId ? <ViewJobButton jobId={controller.lastCreatedJobId} disabled={controller.isBusy} /> : undefined}
        >
          {controller.successMessage}
        </InlineNotice>
      ) : null}
      {view === "migration" && <DataRootMigrationPanel controller={controller} onPathError={onPathError} />}
      {controller.desktopBridgeAvailable ? (
        <DisasterRecoveryPanel controller={controller} onPathError={onPathError} view={view} />
      ) : null}
      {view === "recovery" && <DatabaseBackupTable controller={controller} onPathError={onPathError} />}
    </section>
  );
}
