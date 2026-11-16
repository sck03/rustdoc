import { RefreshCw, ShieldCheck } from "lucide-react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { useOwnershipMaintenance } from "./useOwnershipMaintenance.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";

export function SharedDatabaseOwnershipPanel({
  client,
  canManageUsers,
}: {
  client: ExportDocManagerApiClient;
  canManageUsers: boolean;
}) {
  const { ownershipQuery, message, setMessage, successMessage, setSuccessMessage, fromUserId, setFromUserId, toUserId, setToUserId,
    onlyUnassigned, setOnlyUnassigned, includeInvoices, setIncludeInvoices, includePayments, setIncludePayments,
    includeOtherBusinessData, setIncludeOtherBusinessData, owners, isBusy, canTransfer, handleTransferOwnership } =
    useOwnershipMaintenance(client, canManageUsers);

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
      {message ? <InlineNotice tone="error" title="数据归属改派失败">{message}</InlineNotice> : null}
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
          className="command-button danger"
          type="button"
          disabled={!canTransfer}
          onClick={() => {
            setMessage(null);
            setSuccessMessage(null);
            void handleTransferOwnership();
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
