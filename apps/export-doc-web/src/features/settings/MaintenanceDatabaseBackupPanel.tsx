import { Archive, Download, RefreshCw } from "lucide-react";
import type { ApiPostgreSqlMaintenanceStatusResponse, ApiSharedDatabaseBackupItemDto } from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { formatBytes, formatManagedPath, formatRuntimeDate } from "./settingsFormatters.ts";

export function MaintenanceDatabaseBackupPanel({
  canManageSettings,
  busy,
  status,
  backups,
  isLoading,
  canCreate,
  createPending,
  onRefresh,
  onCreate,
  onDownload,
  onPathError,
}: {
  canManageSettings: boolean;
  busy: boolean;
  status: ApiPostgreSqlMaintenanceStatusResponse | null | undefined;
  backups: ApiSharedDatabaseBackupItemDto[];
  isLoading: boolean;
  canCreate: boolean;
  createPending: boolean;
  onRefresh: () => void;
  onCreate: () => void;
  onDownload: (fileName: string) => void;
  onPathError: (message: string) => void;
}) {
  return (
    <section className="form-section maintenance-database-backup-panel" aria-label="PostgreSQL 数据库备份">
      <div className="section-header">
        <div>
          <h3>数据库备份</h3>
          <p className="section-description">点击“创建备份”，完成后在下表下载，另存到其他设备。换服务器或升级前，请展开“完整迁移与换机”创建包含主密钥和用户模板的完整包。</p>
        </div>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新 PostgreSQL 备份"
            aria-label="刷新 PostgreSQL 备份"
            disabled={!canManageSettings || busy}
            onClick={onRefresh}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="command-button" type="button" disabled={!canCreate} onClick={onCreate}>
            <Archive size={17} aria-hidden="true" />
            <span>{createPending ? "正在后台备份" : "创建备份"}</span>
          </button>
        </div>
      </div>
      {!status?.postgreSqlSelected ? <InlineNotice tone="info">当前为 SQLite 单机模式，PostgreSQL 团队库备份保持停用。</InlineNotice> : null}
      {status?.postgreSqlSelected && !status.postgreSqlConfigured ? <InlineNotice tone="info">PostgreSQL 团队库连接信息尚未完整配置。</InlineNotice> : null}
      {status?.postgreSqlConfigured && !status.toolsReady ? <InlineNotice tone="info">团队数据库维护组件未安装完整，请联系系统管理员或软件服务商处理。</InlineNotice> : null}
      <details className="maintenance-advanced-details"><summary>服务器信息与备份位置</summary><div className="detail-grid runtime-detail-grid">
        <div className="detail-item"><span>团队库模式</span><strong>{status?.postgreSqlConfigured ? "已配置" : status?.postgreSqlSelected ? "未完整" : "未启用"}</strong></div>
        <div className="detail-item"><span>客户端工具</span><strong>{status?.toolsReady ? "已就绪" : "缺失"}</strong></div>
        <div className="detail-item"><span>目标库</span><strong title={status?.database || "-"}>{status?.database || "-"}</strong></div>
        <div className="detail-item"><span>应用账号</span><strong title={status?.username || "-"}>{status?.username || "-"}</strong></div>
        <div className="detail-item detail-item-wide">
          <span>备份目录</span>
          <div className="detail-value-row"><strong title={formatManagedPath(status?.backupRoot)}>{formatManagedPath(status?.backupRoot)}</strong><div className="detail-item-actions">{renderOpenPathAction(status?.backupRoot, "打开 PostgreSQL 备份目录", onPathError)}</div></div>
        </div>
        <div className="detail-item detail-item-wide">
          <span>工具目录</span>
          <div className="detail-value-row"><strong title={formatManagedPath(status?.toolBinRoot)}>{formatManagedPath(status?.toolBinRoot)}</strong><div className="detail-item-actions">{renderOpenPathAction(status?.toolBinRoot, "打开 PostgreSQL 工具目录", onPathError)}</div></div>
        </div>
      </div></details>
      <ResponsiveTableFrame className="backup-table-frame" label="PostgreSQL 团队库备份列表">
        <table className="backup-table" aria-label="PostgreSQL 团队库备份列表">
          <thead><tr><th>文件</th><th>大小</th><th>创建时间</th><th>操作</th></tr></thead>
          <tbody>
            {backups.length > 0 ? backups.map((backup) => (
              <tr key={backup.fullPath || backup.fileName}>
                <td><TablePrimaryText value={backup.fileName} /></td>
                <td>{formatBytes(backup.sizeBytes)}</td>
                <td>{formatRuntimeDate(backup.createdAt)}</td>
                <td><button className="command-button secondary" type="button" disabled={!canManageSettings || busy} aria-label={"下载 " + backup.fileName} onClick={() => onDownload(backup.fileName)}><Download size={17} aria-hidden="true" /><span>下载</span></button></td>
              </tr>
            )) : <tr><td className="empty-cell" colSpan={4}>{isLoading ? "加载中" : "暂无 PostgreSQL 备份"}</td></tr>}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </section>
  );
}
