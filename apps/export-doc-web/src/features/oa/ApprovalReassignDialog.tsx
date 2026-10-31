import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient, OaRequest, UnitTaskReassignment } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import type { OfficeRequestRow } from "../office/officeModel.ts";

export function ApprovalReassignDialog({ client, row, onClose }: { client: ExportDocManagerApiClient; row: OaRequest | OfficeRequestRow; onClose: () => void }) {
  const operation = useOfficeOperation([queryKeys.unitWorkflowTasks(),queryKeys.unitWorkflowSummary()]);
  const accounts = useQuery({ queryKey: ["office","approval-reassign",row.companyScope], queryFn: ({ signal }) => client.getUnitApproverOptions({companyCode:row.companyScope}, { signal }) });
  const requestKind: UnitTaskReassignment["requestKind"] = "meetingRoomId" in row ? "bookings" : "officeSupplyId" in row ? "supply-requests" : `oa-${row.kind}`;
  const waiting = row.approvalPlan?.steps.filter(step => step.status === "Waiting") ?? [];
  const [taskKey, setTaskKey] = useState(waiting[0]?.taskKey ?? "");
  const from=waiting.find(s=>s.taskKey===taskKey)?.approverUserId??0;
  const [to, setTo] = useState(0);
  const [context,setContext] = useState("");
  const contexts = accounts.data?.users.find(user=>user.id===to)?.contexts ?? [];
  const [note, setNote] = useState("");
  return <OfficeDialog title="移交未完成审批" onClose={onClose} {...operation} protectChanges>
    <p>仅替换未完成的审批人，保留已签意见和原审批事件。接任人须有对应查看与审批权限，并符合本人回避和独立复核规则。</p>
    {accounts.isError && <InlineNotice tone="error">{readApiError(accounts.error)}</InlineNotice>}
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => client.reassignUnitWorkflowTask({body:{requestId:row.id,requestKind,expectedVersion:row.versionNumber,fromUserId:from,toUserId:to,toContextId:context,note,taskKey}},{signal}), onClose); }}>
      <fieldset disabled={operation.busy}><OfficeField label="待移交任务"><select required value={taskKey} onChange={event => setTaskKey(event.target.value)}>{waiting.map(step => <option value={step.taskKey} key={step.taskKey}>{row.approvalPlan?.nodes[step.stage].name} · {step.approverName}</option>)}</select></OfficeField>
      <OfficeField label="接任审批人"><select required value={to || ""} onChange={event => {const id=Number(event.target.value);setTo(id);setContext(accounts.data?.users.find(user=>user.id===id)?.contexts[0]?.id ?? "");}}><option value="">选择接任账号</option>{accounts.data?.users.filter(account => account.id !== row.ownerUserId).map(account => <option key={account.id} value={account.id}>{account.fullName} · {account.username}</option>)}</select></OfficeField>
      <OfficeField label="接任身份"><select value={context} onChange={event=>setContext(event.target.value)}>{contexts.map(item=><option key={item.id} value={item.id}>{item.name} · {item.departmentId}</option>)}</select></OfficeField>
      <OfficeField label="移交原因"><textarea required maxLength={500} value={note} onChange={event => setNote(event.target.value)} /></OfficeField></fieldset>
      <OfficeSubmit busy={operation.busy} disabled={!from || !to} label="确认移交审批" />
    </form>
  </OfficeDialog>;
}
