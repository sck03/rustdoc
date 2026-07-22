import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Activity, Archive, Database, LifeBuoy, RefreshCw, RotateCcw, ShieldCheck, Users } from "lucide-react";
import type { ApiHealthResponse, ApiSupportPackageResponse } from "../../api/index.ts";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";
import { parseStringArray } from "./settingsValueUtils.ts";
import { RuntimeDiagnosticsSection } from "./RuntimeDiagnosticsSection.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";

type MaintenanceSectionKey = "postgresql" | "ownership" | "diagnostics" | "support";

export default function MaintenanceSettingsPanels({
  client,
  canManageSettings,
  canManageUsers,
  health,
  healthIsBusy,
  healthErrorMessage,
  initialPanelLabel,
  onRefreshHealth,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  canManageUsers: boolean;
  health: ApiHealthResponse | null;
  healthIsBusy: boolean;
  healthErrorMessage: string | null;
  initialPanelLabel: string;
  onRefreshHealth: () => void;
  onPathError: (message: string) => void;
}) {
  const [activeSection, setActiveSection] = useState<MaintenanceSectionKey>("postgresql");
  const sections = [
    { key: "postgresql" as const, label: "团队库", description: "备份与还原准备", icon: Database },
    { key: "ownership" as const, label: "数据归属", description: "人员变更时改派业务数据", icon: Users },
    { key: "diagnostics" as const, label: "运行检查", description: "检查目录和功能依赖", icon: Activity },
    { key: "support" as const, label: "问题诊断", description: "导出技术支持资料", icon: LifeBuoy },
  ];

  useEffect(() => {
    const normalizedLabel = initialPanelLabel.trim();
    if (!normalizedLabel) return;
    if (normalizedLabel.includes("运行诊断")) setActiveSection("diagnostics");
    else if (normalizedLabel.includes("支持") || normalizedLabel.includes("问题诊断")) setActiveSection("support");
    else if (normalizedLabel.includes("归属") || normalizedLabel.includes("权限改派")) setActiveSection("ownership");
    else if (normalizedLabel.includes("PostgreSQL") || normalizedLabel.includes("团队库")) setActiveSection("postgresql");
  }, [canManageUsers, initialPanelLabel]);

  return (
    <div className="maintenance-workspace">
      <nav className="maintenance-section-nav" aria-label="维护工具分类">
        {sections.map((section) => {
          const Icon = section.icon;
          return (
            <button
              key={section.key}
              className={activeSection === section.key ? "maintenance-section-tab maintenance-section-tab-active" : "maintenance-section-tab"}
              type="button"
              aria-current={activeSection === section.key ? "page" : undefined}
              onClick={() => setActiveSection(section.key)}
            >
              <Icon size={18} aria-hidden="true" />
              <span><strong>{section.label}</strong><small>{section.description}</small></span>
            </button>
          );
        })}
      </nav>
      <div className="maintenance-section-content">
        {activeSection === "postgresql" ? (
          <PostgreSqlMaintenancePanel client={client} canManageSettings={canManageSettings} onPathError={onPathError} />
        ) : null}
        {activeSection === "ownership" ? (
          canManageUsers
            ? <SharedDatabaseOwnershipPanel client={client} canManageUsers={canManageUsers} />
            : <DataOwnershipUnavailablePanel />
        ) : null}
        {activeSection === "diagnostics" ? (
          <RuntimeDiagnosticsSection
            client={client}
            canManageSettings={canManageSettings}
            health={health}
            isBusy={healthIsBusy}
            errorMessage={healthErrorMessage}
            onRefresh={onRefreshHealth}
            onPathError={onPathError}
          />
        ) : null}
        {activeSection === "support" ? (
          <SupportPackagePanel client={client} canManageSettings={canManageSettings} onPathError={onPathError} />
        ) : null}
      </div>
    </div>
  );
}

function DataOwnershipUnavailablePanel() {
  return (
    <section className="form-section shared-ownership-section" aria-label="数据归属说明">
      <div className="section-header">
        <div>
          <h2>数据归属</h2>
          <p className="section-description">用于多人团队发生岗位交接时，把发票和付款报销改派给接手人员。</p>
        </div>
      </div>
      <InlineNotice tone="info">
        当前版本按单机单用户方式使用，不需要进行数据归属改派。启用全功能团队版并由管理员维护账号后，此处会提供来源人员、接手人员和业务范围选择。
      </InlineNotice>
    </section>
  );
}

function PostgreSqlMaintenancePanel({
  client,
  canManageSettings,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  onPathError: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [selectedBackup, setSelectedBackup] = useState("");
  const [targetDatabase, setTargetDatabase] = useState("");
  const [applicationRole, setApplicationRole] = useState("");
  const [oldOwnerRoles, setOldOwnerRoles] = useState("");
  const [lastRestorePlanPath, setLastRestorePlanPath] = useState("");

  const postgreSqlQuery = useQuery({
    queryKey: queryKeys.postgreSqlMaintenanceBackups(),
    queryFn: () => client.listPostgreSqlPhysicalBackups(),
    enabled: canManageSettings,
  });

  useEffect(() => {
    const status = postgreSqlQuery.data?.status;
    if (status) {
      setTargetDatabase((current) => current || status.database || "");
      setApplicationRole((current) => current || status.username || "");
    }

    const backups = postgreSqlQuery.data?.backups ?? [];
    if (backups.length === 0) {
      setSelectedBackup("");
      return;
    }

    if (!selectedBackup || !backups.some((backup) => backup.fileName === selectedBackup)) {
      setSelectedBackup(backups[0].fileName);
    }
  }, [postgreSqlQuery.data, selectedBackup]);

  useEffect(() => {
    if (postgreSqlQuery.isError) {
      setMessage(readApiError(postgreSqlQuery.error));
      setSuccessMessage(null);
    }
  }, [postgreSqlQuery.error, postgreSqlQuery.isError]);

  const createMutation = useMutation({
    mutationFn: () => client.createPostgreSqlPhysicalBackup(),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "PostgreSQL 团队库物理备份已创建。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.postgreSqlMaintenanceBackups() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const restorePlanMutation = useMutation({
    mutationFn: () =>
      client.createPostgreSqlRestorePlan({
        body: {
          backupFileName: selectedBackup,
          targetDatabase: targetDatabase.trim(),
          applicationRole: applicationRole.trim(),
          oldOwnerRoles: parseStringArray(oldOwnerRoles),
        },
      }),
    onSuccess: (response) => {
      setLastRestorePlanPath(response.planRoot);
      setMessage(null);
      setSuccessMessage(response.message || "PostgreSQL 还原与权限改派脚本已生成。");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const status = postgreSqlQuery.data?.status;
  const backups = postgreSqlQuery.data?.backups ?? [];
  const isBusy = postgreSqlQuery.isFetching || createMutation.isPending || restorePlanMutation.isPending;
  const canCreate = canManageSettings && Boolean(status?.postgreSqlConfigured) && Boolean(status?.toolsReady) && !isBusy;
  const canCreatePlan =
    canManageSettings &&
    Boolean(selectedBackup) &&
    Boolean(targetDatabase.trim()) &&
    Boolean(applicationRole.trim()) &&
    !isBusy;

  return (
    <section className="form-section backup-management-section" aria-label="PostgreSQL 团队库维护">
      <div className="section-header">
        <h2>PostgreSQL 团队库维护</h2>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新 PostgreSQL 备份" aria-label="刷新 PostgreSQL 备份"
            disabled={!canManageSettings || isBusy}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              void postgreSqlQuery.refetch();
            }}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button
            className="command-button"
            type="button"
            disabled={!canCreate}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              createMutation.mutate();
            }}
          >
            <Archive size={17} aria-hidden="true" />
            <span>创建物理备份</span>
          </button>
        </div>
      </div>
      {!status?.postgreSqlSelected ? <InlineNotice tone="info">当前为 SQLite 单机模式，PostgreSQL 团队库维护保持停用。</InlineNotice> : null}
      {status?.postgreSqlSelected && !status.postgreSqlConfigured ? <InlineNotice tone="info">PostgreSQL 团队库连接信息尚未完整配置。</InlineNotice> : null}
      {status?.postgreSqlConfigured && !status.toolsReady ? (
        <InlineNotice tone="info">未发现完整 PostgreSQL 客户端工具。请把 pg_dump、pg_restore、psql 放入程序根 Tools/PostgreSQL/bin。</InlineNotice>
      ) : null}
      {message ? <InlineNotice tone="error" title="数据库维护失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <div className="detail-grid runtime-detail-grid">
        <div className="detail-item">
          <span>团队库模式</span>
          <strong>{status?.postgreSqlConfigured ? "已配置" : status?.postgreSqlSelected ? "未完整" : "未启用"}</strong>
        </div>
        <div className="detail-item">
          <span>客户端工具</span>
          <strong>{status?.toolsReady ? "已就绪" : "缺失"}</strong>
        </div>
        <div className="detail-item">
          <span>目标库</span>
          <strong title={status?.database || "-"}>{status?.database || "-"}</strong>
        </div>
        <div className="detail-item">
          <span>应用账号</span>
          <strong title={status?.username || "-"}>{status?.username || "-"}</strong>
        </div>
        <div className="detail-item detail-item-wide">
          <span>物理备份目录</span>
          <div className="detail-value-row">
            <strong title={status?.backupRoot || "-"}>{status?.backupRoot || "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(status?.backupRoot, "打开 PostgreSQL 备份目录", onPathError)}</div>
          </div>
        </div>
        <div className="detail-item detail-item-wide">
          <span>工具目录</span>
          <div className="detail-value-row">
            <strong title={status?.toolBinRoot || "-"}>{status?.toolBinRoot || "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(status?.toolBinRoot, "打开 PostgreSQL 工具目录", onPathError)}</div>
          </div>
        </div>
      </div>
      <div className="backup-action-grid">
        <SelectField
          label="备份文件"
          value={selectedBackup}
          disabled={!canManageSettings || isBusy || backups.length === 0}
          options={backups.map((backup) => ({ value: backup.fileName, label: backup.fileName }))}
          onChange={setSelectedBackup}
        />
        <label>
          <span>目标数据库</span>
          <input
            value={targetDatabase}
            disabled={!canManageSettings || isBusy}
            onChange={(event) => setTargetDatabase(event.target.value)}
          />
        </label>
        <label>
          <span>应用账号</span>
          <input
            value={applicationRole}
            disabled={!canManageSettings || isBusy}
            onChange={(event) => setApplicationRole(event.target.value)}
          />
        </label>
        <button
          className="command-button"
          type="button"
          disabled={!canCreatePlan}
          onClick={() => {
            setMessage(null);
            setSuccessMessage(null);
            restorePlanMutation.mutate();
          }}
        >
          <RotateCcw size={17} aria-hidden="true" />
          <span>生成还原脚本</span>
        </button>
      </div>
      <details className="maintenance-advanced-details">
        <summary>高级还原选项</summary>
        <label className="textarea-field settings-textarea-field">
          <span>原数据库所有者（可选）</span>
          <textarea
            value={oldOwnerRoles}
            disabled={!canManageSettings || isBusy}
            placeholder={"每行填写一个原账号"}
            onChange={(event) => setOldOwnerRoles(event.target.value)}
          />
          <small>仅在数据库从其他账号迁移而来时填写。</small>
        </label>
      </details>
      {lastRestorePlanPath ? (
        <InlineNotice tone="info" action={renderOpenPathAction(lastRestorePlanPath, "打开还原计划目录", onPathError)}>{lastRestorePlanPath}</InlineNotice>
      ) : null}
      <ResponsiveTableFrame className="backup-table-frame" label="PostgreSQL 团队库物理备份列表">
        <table className="backup-table" aria-label="PostgreSQL 团队库物理备份列表">
          <thead>
            <tr>
              <th>文件</th>
              <th>大小</th>
              <th>创建时间</th>
              <th>路径</th>
            </tr>
          </thead>
          <tbody>
            {backups.length > 0 ? (
              backups.map((backup) => (
                <tr key={backup.fullPath || backup.fileName}>
                  <td>{backup.fileName}</td>
                  <td>{formatBytes(backup.sizeBytes)}</td>
                  <td>{formatRuntimeDate(backup.createdAt)}</td>
                  <td>
                    <div className="table-path-cell">
                      <span title={backup.fullPath}>{backup.fullPath || "-"}</span>
                      {renderOpenPathAction(backup.fullPath, "打开 PostgreSQL 备份", onPathError)}
                    </div>
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td className="empty-cell" colSpan={4}>
                  {postgreSqlQuery.isFetching ? "加载中" : "暂无 PostgreSQL 物理备份"}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </section>
  );
}

function SupportPackagePanel({
  client,
  canManageSettings,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  onPathError: (message: string) => void;
}) {
  const requestConfirmation = useConfirmation();
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [lastPackage, setLastPackage] = useState<ApiSupportPackageResponse | null>(null);
  const [includeLatestDatabaseBackup, setIncludeLatestDatabaseBackup] = useState(false);
  const [includeSampleFiles, setIncludeSampleFiles] = useState(false);
  const includeOptionalFiles = includeLatestDatabaseBackup || includeSampleFiles;
  const isDesktop = isDesktopBridgeAvailable();

  const createMutation = useMutation({
    mutationFn: async () => {
      const body = {
          includeLatestDatabaseBackup,
          includeSampleFiles,
          confirmationText: includeOptionalFiles ? "INCLUDE OPTIONAL FILES" : "",
      };
      if (isDesktop) {
        const response = await client.saveSupportPackageToRuntime({ body });
        return { mode: "desktop" as const, response };
      }

      const blob = await client.downloadSupportPackage({ body });
      downloadBlob(blob, `support-package-${new Date().toISOString().replace(/[:.]/g, "-")}.zip`);
      return { mode: "browser" as const };
    },
    onSuccess: (result) => {
      setLastPackage(result.mode === "desktop" ? result.response : null);
      setMessage(null);
      setSuccessMessage(result.mode === "desktop" ? (result.response.message || "支持包已导出。") : "支持包已交给浏览器下载。");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });
  const supportPackageCanCreate = canManageSettings && !createMutation.isPending;

  async function handleCreateSupportPackage() {
    if (includeOptionalFiles && !await requestConfirmation({ title: "生成包含可选文件的支持包", description: "支持包将包含所选的数据库备份或样张文件。", details: ["请确认其中不含不应交给技术支持的敏感业务资料。"], confirmLabel: "确认生成" })) {
      return;
    }

    createMutation.mutate();
  }

  return (
    <section className="form-section support-package-section" aria-label="问题诊断包">
      <div className="section-header">
        <div>
          <h2>问题诊断包</h2>
          <p className="section-description">遇到无法启动、报表异常等问题时，可导出资料交给技术支持。</p>
        </div>
        <div className="toolbar-actions">
          <button
            className="command-button"
            type="button"
            disabled={!supportPackageCanCreate}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              handleCreateSupportPackage();
            }}
          >
            <LifeBuoy size={17} aria-hidden="true" />
            <span>导出支持包</span>
          </button>
        </div>
      </div>
      {message ? <InlineNotice tone="error" title="数据归属维护失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <div className="backup-action-grid">
        <label className="settings-check">
          <input
            type="checkbox"
            checked={includeLatestDatabaseBackup}
            disabled={!canManageSettings || createMutation.isPending}
            onChange={(event) => setIncludeLatestDatabaseBackup(event.target.checked)}
          />
          <span>包含最新数据库备份</span>
        </label>
        <label className="settings-check">
          <input
            type="checkbox"
            checked={includeSampleFiles}
            disabled={!canManageSettings || createMutation.isPending}
            onChange={(event) => setIncludeSampleFiles(event.target.checked)}
          />
          <span>包含样张文件</span>
        </label>
      </div>
      {isDesktop ? <div className="detail-grid runtime-detail-grid">
        <div className="detail-item detail-item-wide">
          <span>支持包目录</span>
          <div className="detail-value-row">
            <strong title={lastPackage?.supportPackageRoot || "-"}>{lastPackage?.supportPackageRoot || "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(lastPackage?.supportPackageRoot, "打开支持包目录", onPathError)}</div>
          </div>
        </div>
        <div className="detail-item detail-item-wide">
          <span>最近支持包</span>
          <div className="detail-value-row">
            <strong title={lastPackage?.fullPath || "-"}>{lastPackage ? `${lastPackage.fileName} (${formatBytes(lastPackage.sizeBytes)})` : "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(lastPackage?.fullPath, "打开支持包", onPathError)}</div>
          </div>
        </div>
      </div> : null}
    </section>
  );
}

function SharedDatabaseOwnershipPanel({
  client,
  canManageUsers,
}: {
  client: ExportDocManagerApiClient;
  canManageUsers: boolean;
}) {
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [fromUserId, setFromUserId] = useState("");
  const [toUserId, setToUserId] = useState("");
  const [onlyUnassigned, setOnlyUnassigned] = useState(true);
  const [includeInvoices, setIncludeInvoices] = useState(true);
  const [includePayments, setIncludePayments] = useState(true);
  const [includeOtherBusinessData, setIncludeOtherBusinessData] = useState(true);

  const ownershipQuery = useQuery({
    queryKey: queryKeys.sharedDatabaseOwnership(),
    queryFn: () => client.getSharedDatabaseOwnershipSummary(),
    enabled: canManageUsers,
  });

  useEffect(() => {
    const owners = ownershipQuery.data?.owners ?? [];
    const activeOwners = owners.filter((owner) => owner.isActive);
    if ((!toUserId || !activeOwners.some((owner) => String(owner.userId) === toUserId)) && activeOwners.length > 0) {
      setToUserId(String(activeOwners[0].userId));
    }
  }, [ownershipQuery.data, toUserId]);

  useEffect(() => {
    if (ownershipQuery.isError) {
      setMessage(readApiError(ownershipQuery.error));
      setSuccessMessage(null);
    }
  }, [ownershipQuery.error, ownershipQuery.isError]);

  const transferMutation = useMutation({
    mutationFn: () =>
      client.transferSharedDatabaseOwnership({
        body: {
          fromUserId: onlyUnassigned || !fromUserId ? undefined : Number(fromUserId),
          toUserId: Number(toUserId),
          includeInvoices,
          includePayments,
          includeOtherBusinessData,
          onlyUnassigned,
          departmentId: "",
          companyScope: "",
          confirmationText: "TRANSFER OWNERSHIP",
        },
      }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "归属改派完成。");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.sharedDatabaseOwnership() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.paymentsRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.crmDashboard() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.containerPackingProjects() }),
        queryClient.invalidateQueries({ queryKey: ["reports", "user-templates"] }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const owners = ownershipQuery.data?.owners ?? [];
  const isBusy = ownershipQuery.isFetching || transferMutation.isPending;
  const canTransfer =
    canManageUsers &&
    Number(toUserId) > 0 &&
    (includeInvoices || includePayments || includeOtherBusinessData) &&
    (ownershipQuery.data?.owners ?? []).some((owner) => owner.isActive && String(owner.userId) === toUserId) &&
    (onlyUnassigned || !fromUserId || fromUserId !== toUserId) &&
    !isBusy;

  async function handleTransferOwnership() {
    const sourceLabel = onlyUnassigned
      ? "当前未归属的数据"
      : owners.find((owner) => String(owner.userId) === fromUserId)?.username || "所选用户的数据";
    const targetLabel = owners.find((owner) => String(owner.userId) === toUserId)?.username || "目标用户";
    const scopes = [includeInvoices ? "发票" : "", includePayments ? "付款报销" : "", includeOtherBusinessData ? "其他业务资料" : ""]
      .filter(Boolean)
      .join("、");
    if (!await requestConfirmation({ title: "改派业务数据归属", description: `即将把${sourceLabel}中的${scopes}改派给“${targetLabel}”。`, details: ["此操作会修改业务数据归属，并写入审计记录。"], confirmLabel: "确认改派", tone: "danger" })) {
      return;
    }

    transferMutation.mutate();
  }

  return (
    <section className="form-section shared-ownership-section" aria-label="数据归属改派">
      <div className="section-header">
        <div>
          <h2>数据归属改派</h2>
          <p className="section-description">用于员工交接或补齐历史数据归属，不会移动附件和导出文件。</p>
        </div>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新归属统计" aria-label="刷新归属统计"
            disabled={!canManageUsers || isBusy}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              void ownershipQuery.refetch();
            }}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>
      {message ? <InlineNotice tone="error" title="支持包操作失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <div className="detail-grid runtime-detail-grid">
        <div className="detail-item">
          <span>发票总数</span>
          <strong>{ownershipQuery.data?.totalInvoices ?? 0}</strong>
        </div>
        <div className="detail-item">
          <span>未归属发票</span>
          <strong>{ownershipQuery.data?.unassignedInvoices ?? 0}</strong>
        </div>
        <div className="detail-item">
          <span>付款报销总数</span>
          <strong>{ownershipQuery.data?.totalPayments ?? 0}</strong>
        </div>
        <div className="detail-item">
          <span>未归属付款</span>
          <strong>{ownershipQuery.data?.unassignedPayments ?? 0}</strong>
        </div>
        <div className="detail-item">
          <span>其他业务资料总数</span>
          <strong>{ownershipQuery.data?.totalOtherBusinessData ?? 0}</strong>
        </div>
        <div className="detail-item">
          <span>未归属其他资料</span>
          <strong>{ownershipQuery.data?.unassignedOtherBusinessData ?? 0}</strong>
        </div>
      </div>
      <div className="backup-action-grid shared-ownership-action-grid">
        <SelectField
          label="来源用户"
          value={fromUserId}
          disabled={!canManageUsers || isBusy || onlyUnassigned}
          options={[
            { value: "", label: "全部用户" },
            ...owners.map((owner) => ({ value: String(owner.userId), label: `${owner.username} · ${owner.isActive ? "启用" : "停用"} (${owner.invoiceCount}/${owner.paymentCount}/${owner.otherBusinessDataCount})` })),
          ]}
          onChange={setFromUserId}
        />
        <SelectField
          label="改派给"
          value={toUserId}
          disabled={!canManageUsers || isBusy || owners.length === 0}
          options={owners.filter((owner) => owner.isActive).map((owner) => ({ value: String(owner.userId), label: `${owner.username} · ${owner.departmentId || "-"} / ${owner.companyScope || "-"}` }))}
          onChange={setToUserId}
        />
        <label className="settings-check">
          <input type="checkbox" checked={onlyUnassigned} disabled={!canManageUsers || isBusy} onChange={(event) => setOnlyUnassigned(event.target.checked)} />
          <span>仅改派未归属</span>
        </label>
        <label className="settings-check">
          <input type="checkbox" checked={includeInvoices} disabled={!canManageUsers || isBusy} onChange={(event) => setIncludeInvoices(event.target.checked)} />
          <span>发票</span>
        </label>
        <label className="settings-check">
          <input type="checkbox" checked={includePayments} disabled={!canManageUsers || isBusy} onChange={(event) => setIncludePayments(event.target.checked)} />
          <span>付款报销</span>
        </label>
        <label className="settings-check">
          <input type="checkbox" checked={includeOtherBusinessData} disabled={!canManageUsers || isBusy} onChange={(event) => setIncludeOtherBusinessData(event.target.checked)} />
          <span>其他业务资料</span>
        </label>
        <button
          className="command-button danger-command"
          type="button"
          disabled={!canTransfer}
          onClick={() => {
            setMessage(null);
            setSuccessMessage(null);
            handleTransferOwnership();
          }}
        >
          <ShieldCheck size={17} aria-hidden="true" />
          <span>执行改派</span>
        </button>
      </div>
      <ResponsiveTableFrame className="backup-table-frame" label="共享库归属统计">
        <table className="backup-table" aria-label="共享库归属统计">
          <thead>
            <tr>
              <th>用户</th>
              <th>角色</th>
              <th>部门</th>
              <th>公司范围</th>
              <th>发票</th>
              <th>付款报销</th>
              <th>其他业务</th>
            </tr>
          </thead>
          <tbody>
            {owners.length > 0 ? (
              owners.map((owner) => (
                <tr key={owner.userId}>
                  <td>{owner.username}</td>
                  <td>{owner.role}</td>
                  <td>{owner.departmentId || "-"}</td>
                  <td>{owner.companyScope || "-"}</td>
                  <td>{owner.invoiceCount}</td>
                  <td>{owner.paymentCount}</td>
                  <td>{owner.otherBusinessDataCount}</td>
                </tr>
              ))
            ) : (
              <tr>
                <td className="empty-cell" colSpan={7}>
                  {canManageUsers ? (ownershipQuery.isFetching ? "加载中" : "暂无用户") : "无权限"}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </section>
  );
}
