import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { oaApi } from "./oaApi.ts";

export function ApprovalReassignDialog({ client, row, onClose }: { client: ExportDocManagerApiClient; row: OaRequest; onClose: () => void }) {
  const operation = useOfficeOperation();
  const accounts = useQuery({ queryKey: queryKeys.users(), queryFn: ({ signal }) => client.listUsers({ signal }) });
  const waiting = row.approvalPlan?.steps.filter(step => step.status === "Waiting") ?? [];
  const [from, setFrom] = useState(waiting[0]?.approverUserId ?? 0);
  const [to, setTo] = useState(0);
  const [note, setNote] = useState("");
  return <OfficeDialog title="移交未完成审批" onClose={onClose} {...operation} protectChanges>
    <p>仅替换未完成的审批人，保留已签意见和原审批事件。接任人须有对应查看与审批范围，不能自批或重复签署。</p>
    {accounts.isError && <InlineNotice tone="error">{readApiError(accounts.error)}</InlineNotice>}
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => oaApi(client, row.kind).reassign(row.id, { expectedVersion: row.versionNumber, fromUserId: from, toUserId: to, note }, { signal }), onClose); }}>
      <fieldset disabled={operation.busy}><OfficeField label="原审批人"><select required value={from || ""} onChange={event => setFrom(Number(event.target.value))}>{waiting.map(step => <option value={step.approverUserId} key={step.approverUserId}>{step.approverName}</option>)}</select></OfficeField>
      <OfficeField label="接任审批人"><select required value={to || ""} onChange={event => setTo(Number(event.target.value))}><option value="">选择接任账号</option>{accounts.data?.users.filter(account => account.isActive && account.companyScope === row.companyScope && account.id !== row.ownerUserId && !row.approvalPlan?.steps.some(step => step.approverUserId === account.id || step.actedByUserId === account.id)).map(account => <option key={account.id} value={account.id}>{account.fullName} · {account.username}</option>)}</select></OfficeField>
      <OfficeField label="移交原因"><textarea required maxLength={500} value={note} onChange={event => setNote(event.target.value)} /></OfficeField></fieldset>
      <OfficeSubmit busy={operation.busy} disabled={!from || !to} label="确认移交审批" />
    </form>
  </OfficeDialog>;
}
