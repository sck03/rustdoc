import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { usePostgreSqlMaintenance } from "./usePostgreSqlMaintenance.ts";
import { MaintenanceBackgroundTaskStatusPanel } from "./MaintenanceBackgroundTaskStatusPanel.tsx";
import { MaintenanceDatabaseBackupPanel } from "./MaintenanceDatabaseBackupPanel.tsx";
import { MaintenanceDatabaseRecoveryPanel } from "./MaintenanceDatabaseRecoveryPanel.tsx";
import { MaintenanceFullMigrationPanel } from "./MaintenanceFullMigrationPanel.tsx";
import { MaintenanceOperationsGuide } from "./MaintenanceOperationsGuide.tsx";

export function PostgreSqlMaintenancePanel({
  client,
  canManageSettings,
  onPathError,
  view = "all",
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  onPathError: (message: string) => void;
  view?: "migration" | "recovery" | "all";
}) {
  const {
    message,
    successMessage,
    lastRestorePlanPath,
    databaseDetails,
    migrationDetails,
    postgreSqlQuery,
    status,
    backups,
    migrationStatus,
    isBusy,
    canCreate,
    canCreatePlan,
    canCreateMigration,
    canRestoreMigration,
    canRestoreDatabase,
    canUploadDatabase,
    createMutation,
    restorePlanMutation,
    downloadMigrationMutation,
    restoreMigrationMutation,
    databaseRestoreMutation,
    downloadDatabaseMutation,
    uploadDatabaseRestoreMutation,
    clearFeedback,
    selectedBackup,
    targetDatabase,
    applicationRole,
    oldOwnerRoles,
    databaseAdminPassword,
    databaseRestoreConfirmation,
    databaseUploadFile,
    migrationAdminPassword,
    migrationPassword,
    migrationPasswordConfirmation,
    migrationCreateConfirmation,
    migrationRestoreFile,
    migrationRestorePassword,
    migrationRestoreConfirmation,
    setSelectedBackup,
    setTargetDatabase,
    setApplicationRole,
    setOldOwnerRoles,
    setDatabaseAdminPassword,
    setDatabaseRestoreConfirmation,
    setDatabaseUploadFile,
    setMigrationAdminPassword,
    setMigrationPassword,
    setMigrationPasswordConfirmation,
    setMigrationCreateConfirmation,
    setMigrationRestoreFile,
    setMigrationRestorePassword,
    setMigrationRestoreConfirmation,
  } = usePostgreSqlMaintenance(client, canManageSettings, view);

  return (
    <section className="form-section backup-management-section" aria-label="PostgreSQL 团队库维护">
      <div className="section-header">
        <h2>{view === "migration" ? "整机迁移" : "数据库备份与外部文件恢复"}</h2>
      </div>
      {message ? (
        <InlineNotice tone="error" title="数据库维护失败">
          {message}
        </InlineNotice>
      ) : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <MaintenanceBackgroundTaskStatusPanel status={migrationStatus} />
      <MaintenanceOperationsGuide pending={Boolean(migrationStatus?.pendingRestore)} />
      {view !== "migration" && (
        <MaintenanceDatabaseBackupPanel
          canManageSettings={canManageSettings}
          busy={isBusy}
          status={status}
          backups={backups}
          isLoading={postgreSqlQuery.isFetching}
          canCreate={canCreate}
          createPending={createMutation.isPending}
          onRefresh={() => {
            clearFeedback();
            void postgreSqlQuery.refetch();
          }}
          onCreate={() => {
            clearFeedback();
            createMutation.mutate();
          }}
          onDownload={(fileName) => downloadDatabaseMutation.mutate(fileName)}
          onPathError={onPathError}
        />
      )}
      {view !== "migration" && (
        <details ref={databaseDetails} className="maintenance-advanced-details" aria-label="恢复数据库">
          <summary>恢复数据库（回到某份备份）</summary>
          <MaintenanceDatabaseRecoveryPanel
            canManageSettings={canManageSettings}
            busy={isBusy}
            backups={backups}
            selectedBackup={selectedBackup}
            targetDatabase={targetDatabase}
            applicationRole={applicationRole}
            oldOwnerRoles={oldOwnerRoles}
            adminPassword={databaseAdminPassword}
            restoreConfirmation={databaseRestoreConfirmation}
            uploadFile={databaseUploadFile}
            canRestoreSelected={canRestoreDatabase}
            canRestoreUploaded={canUploadDatabase}
            canCreatePlan={canCreatePlan}
            onSelectedBackupChange={(value) => {
              setSelectedBackup(value);
              setDatabaseAdminPassword("");
              setDatabaseRestoreConfirmation("");
            }}
            onTargetDatabaseChange={setTargetDatabase}
            onApplicationRoleChange={setApplicationRole}
            onOldOwnerRolesChange={setOldOwnerRoles}
            onAdminPasswordChange={setDatabaseAdminPassword}
            onRestoreConfirmationChange={setDatabaseRestoreConfirmation}
            onUploadFileChange={(file) => {
              setDatabaseUploadFile(file);
              setDatabaseAdminPassword("");
              setDatabaseRestoreConfirmation("");
            }}
            onDownload={() => downloadDatabaseMutation.mutate(selectedBackup)}
            onRestoreSelected={() => databaseRestoreMutation.mutate()}
            onRestoreUploaded={() => uploadDatabaseRestoreMutation.mutate()}
            onCreatePlan={() => {
              clearFeedback();
              restorePlanMutation.mutate();
            }}
          />
        </details>
      )}
      <details
        ref={migrationDetails}
        open={view === "migration" || undefined}
        className="maintenance-advanced-details"
        aria-label="完整迁移与换机"
      >
        <summary>{view === "recovery" ? "从外部完整包恢复" : "完整迁移与换机"}</summary>
        <MaintenanceFullMigrationPanel
          view={view}
          canManageSettings={canManageSettings}
          busy={isBusy}
          status={migrationStatus}
          adminPassword={migrationAdminPassword}
          packagePassword={migrationPassword}
          packagePasswordConfirmation={migrationPasswordConfirmation}
          createConfirmation={migrationCreateConfirmation}
          restoreFile={migrationRestoreFile}
          restorePassword={migrationRestorePassword}
          restoreConfirmation={migrationRestoreConfirmation}
          canCreate={canCreateMigration}
          canRestore={canRestoreMigration}
          createPending={downloadMigrationMutation.isPending}
          onAdminPasswordChange={setMigrationAdminPassword}
          onPackagePasswordChange={setMigrationPassword}
          onPackagePasswordConfirmationChange={setMigrationPasswordConfirmation}
          onCreateConfirmationChange={setMigrationCreateConfirmation}
          onRestoreFileChange={(file) => {
            setMigrationRestoreFile(file);
            setMigrationAdminPassword("");
            setMigrationRestorePassword("");
            setMigrationRestoreConfirmation("");
          }}
          onRestorePasswordChange={setMigrationRestorePassword}
          onRestoreConfirmationChange={setMigrationRestoreConfirmation}
          onCreate={() => downloadMigrationMutation.mutate()}
          onRestore={() => restoreMigrationMutation.mutate()}
        />
      </details>
      {lastRestorePlanPath ? (
        <InlineNotice tone="info" action={renderOpenPathAction(lastRestorePlanPath, "打开还原计划目录", onPathError)}>
          {lastRestorePlanPath}
        </InlineNotice>
      ) : null}
    </section>
  );
}
