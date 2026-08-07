import { Archive, RefreshCw } from "lucide-react";
import type { ApiPostgreSqlMaintenanceStatusResponse, ApiSharedDatabaseBackupItemDto } from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";

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
  onPathError: (message: string) => void;
}) {
  return (
    <section className="form-section maintenance-database-backup-panel" aria-label="PostgreSQL 数据库备份">
      <div className="section-header">
        <div>
          <h3>数据库备份</h3>
          <p className="section-description">物理备份写入运行目录，可按需下载或用于恢复；备份过程在后台执行。</p>
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
            <span>{createPending ? "正在后台备份" : "创建物理备份"}</span>
          </button>
        </div>
      </div>
      {!status?.postgreSqlSelected ? <InlineNotice tone="info">当前为 SQLite 单机模式，PostgreSQL 团队库备份保持停用。</InlineNotice> : null}
      {status?.postgreSqlSelected && !status.postgreSqlConfigured ? <InlineNotice tone="info">PostgreSQL 团队库连接信息尚未完整配置。</InlineNotice> : null}
      {status?.postgreSqlConfigured && !status.toolsReady ? <InlineNotice tone="info">团队数据库维护组件未安装完整，请联系系统管理员或软件服务商处理。</InlineNotice> : null}
      <div className="detail-grid runtime-detail-grid">
        <div className="detail-item"><span>团队库模式</span><strong>{status?.postgreSqlConfigured ? "已配置" : status?.postgreSqlSelected ? "未完整" : "未启用"}</strong></div>
        <div className="detail-item"><span>客户端工具</span><strong>{status?.toolsReady ? "已就绪" : "缺失"}</strong></div>
        <div className="detail-item"><span>目标库</span><strong title={status?.database || "-"}>{status?.database || "-"}</strong></div>
        <div className="detail-item"><span>应用账号</span><strong title={status?.username || "-"}>{status?.username || "-"}</strong></div>
        <div className="detail-item detail-item-wide">
          <span>物理备份目录</span>
          <div className="detail-value-row"><strong title={status?.backupRoot || "-"}>{status?.backupRoot || "-"}</strong><div className="detail-item-actions">{renderOpenPathAction(status?.backupRoot, "打开 PostgreSQL 备份目录", onPathError)}</div></div>
        </div>
        <div className="detail-item detail-item-wide">
          <span>工具目录</span>
          <div className="detail-value-row"><strong title={status?.toolBinRoot || "-"}>{status?.toolBinRoot || "-"}</strong><div className="detail-item-actions">{renderOpenPathAction(status?.toolBinRoot, "打开 PostgreSQL 工具目录", onPathError)}</div></div>
        </div>
      </div>
      <ResponsiveTableFrame className="backup-table-frame" label="PostgreSQL 团队库物理备份列表">
        <table className="backup-table" aria-label="PostgreSQL 团队库物理备份列表">
          <thead><tr><th>文件</th><th>大小</th><th>创建时间</th><th>路径</th></tr></thead>
          <tbody>
            {backups.length > 0 ? backups.map((backup) => (
              <tr key={backup.fullPath || backup.fileName}>
                <td>{backup.fileName}</td>
                <td>{formatBytes(backup.sizeBytes)}</td>
                <td>{formatRuntimeDate(backup.createdAt)}</td>
                <td><div className="table-path-cell"><span title={backup.fullPath}>{backup.fullPath || "-"}</span>{renderOpenPathAction(backup.fullPath, "打开 PostgreSQL 备份", onPathError)}</div></td>
              </tr>
            )) : <tr><td className="empty-cell" colSpan={4}>{isLoading ? "加载中" : "暂无 PostgreSQL 物理备份"}</td></tr>}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </section>
  );
}
