import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useConfirmation } from "../../ui/confirmationContext.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";

export function useOwnershipMaintenance(client: ExportDocManagerApiClient, canManageUsers: boolean) {
  const requestConfirmation = useConfirmation();
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

  const scope = JSON.stringify([canManageUsers, fromUserId, toUserId, onlyUnassigned, includeInvoices, includePayments, includeOtherBusinessData]);
  const transferMutation = useScopedMutation(scope, client, {
    mutationFn: async (_: void, signal, { checkCurrent }) => {
      if (!canTransfer) return null;
      const body = { fromUserId: onlyUnassigned || !fromUserId ? null : Number(fromUserId), toUserId: Number(toUserId),
        includeInvoices, includePayments, includeOtherBusinessData, onlyUnassigned,
        departmentId: "", companyScope: "", confirmationText: "TRANSFER OWNERSHIP" };
      const sourceLabel = onlyUnassigned
        ? "当前未归属的数据"
        : owners.find((owner) => String(owner.userId) === fromUserId)?.username || "所选用户的数据";
      const targetLabel = owners.find((owner) => String(owner.userId) === toUserId)?.username || "目标用户";
      const scopes = [includeInvoices ? "发票" : "", includePayments ? "付款报销" : "", includeOtherBusinessData ? "其他业务资料" : ""]
        .filter(Boolean)
        .join("、");
      if (!await requestConfirmation({ title: "改派业务数据归属", description: `即将把${sourceLabel}中的${scopes}改派给“${targetLabel}”。`,
        details: ["此操作会修改业务数据归属，并写入审计记录。"], confirmLabel: "确认改派", tone: "danger" })) return null;
      checkCurrent();
      return client.transferSharedDatabaseOwnership({ body }, { signal });
    },
    onSuccess: response => { if (response) { setMessage(null); setSuccessMessage(response.message || "归属改派完成。"); } },
    refreshKeys: response => response ? [queryKeys.sharedDatabaseOwnership(), queryKeys.invoicesRoot(), queryKeys.queryInvoicesRoot(),
      queryKeys.paymentsRoot(), queryKeys.crmDashboard(), queryKeys.containerPackingProjects(), ["reports", "user-templates"]] : [],
    onError: error => { setMessage(readApiError(error)); setSuccessMessage(null); },
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

  return { ownershipQuery, message, setMessage, successMessage, setSuccessMessage, fromUserId, setFromUserId, toUserId, setToUserId,
    onlyUnassigned, setOnlyUnassigned, includeInvoices, setIncludeInvoices, includePayments, setIncludePayments,
    includeOtherBusinessData, setIncludeOtherBusinessData, owners, isBusy, canTransfer,
    handleTransferOwnership: () => transferMutation.mutate() };
}
