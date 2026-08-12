import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, ShieldCheck } from "lucide-react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";

export function SharedDatabaseOwnershipPanel({
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
    queryFn: ({ signal }) => client.getSharedDatabaseOwnershipSummary({ signal }),
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
          fromUserId: onlyUnassigned || !fromUserId ? null : Number(fromUserId),
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
          className="command-button danger-command"
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
