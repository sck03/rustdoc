import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { DataRootMigrationPanel } from "./DataRootMigrationPanel.tsx";
import { DatabaseBackupPanel } from "./DatabaseBackupPanel.tsx";
import { DatabaseBackupTable } from "./DatabaseBackupTable.tsx";
import { DisasterRecoveryPanel } from "./DisasterRecoveryPanel.tsx";
import { useBackupManagement } from "./useBackupManagement.ts";

export default function BackupManagementPanel({
  client,
  canManageSettings,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  onPathError: (message: string) => void;
}) {
  const controller = useBackupManagement(client, canManageSettings);

  return (
    <section className="form-section backup-management-section" aria-label="数据备份与还原">
      <DatabaseBackupPanel controller={controller} onPathError={onPathError} />
      {controller.message ? <InlineNotice tone="error" title="备份操作失败">{controller.message}</InlineNotice> : null}
      {controller.successMessage ? <InlineNotice tone="success">{controller.successMessage}</InlineNotice> : null}
      <DataRootMigrationPanel controller={controller} onPathError={onPathError} />
      {controller.desktopBridgeAvailable ? (
        <DisasterRecoveryPanel controller={controller} onPathError={onPathError} />
      ) : null}
      <DatabaseBackupTable controller={controller} onPathError={onPathError} />
    </section>
  );
}
