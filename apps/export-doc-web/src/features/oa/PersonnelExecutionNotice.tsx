import { useState } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { officeCommand } from "../office/officeCommands.ts";
import { useOaOperation } from "./useOaOperation.ts";
import { oaAccess } from "./oaModel.ts";

export function PersonnelExecutionNotice({ client, user, row }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: OaRequest }) {
  const operation = useOaOperation("personnel");
  const [action, setAction] = useState<"execute" | "cancel" | null>(null);
  const [note, setNote] = useState("");
  const execution = row.personnelExecution;
  if (row.kind !== "personnel" || row.status !== "Approved" || !execution) return null;
  const blocked = execution.state === "Blocked";
  const due = Date.parse(execution.effectiveAt) <= Date.now();
  const businessSigner = user.role !== "Admin" && Boolean(user.employee && ["Active", "Probation"].includes(user.employee.status));
  const canCancel = oaAccess(user, "personnel", "cancel", row) && (row.ownerUserId === user.id || user.capabilities.permissions.some(g => g.resourceKey === "organization.workflows" && g.action === "manage"));
  const label = action === "cancel" ? "取消尚未生效的变动" : "重新核验并执行";
  const reasons: Record<string, string> = { Execution400: "人员或组织条件已不满足。", Execution401: "执行身份已失效。", Execution403: "申请人或执行人已失去所需权限。", Execution404: "关联人员或组织记录已不存在。", Execution409: "人员版本或交接状态发生变化。" };
  return <>
    <InlineNotice tone={blocked ? "error" : "info"} title={blocked ? "人员变动执行受阻" : "已批准，等待生效"}>
      生效时间：{formatBusinessDateTime(execution.effectiveAt, user.businessTimeZone)}。
      {blocked ? `${reasons[execution.errorCode] ?? "执行条件需要重新核验。"}请完成权限或交接处理后重试；人员事实已改变时，请取消本次执行并重新申请。` : "系统将在生效时间到达后核验人员版本、权限和交接状态，再更新档案。"}
    </InlineNotice>
    <div className="office-card-actions">
      {due && businessSigner && oaAccess(user, "personnel", "approve", row) && <button type="button" className="command-button secondary" onClick={() => setAction("execute")}>重新核验并执行</button>}
      {canCancel && <button type="button" className="command-button secondary" onClick={() => setAction("cancel")}>取消尚未生效的变动</button>}
    </div>
    {action && <OfficeDialog title={label} onClose={() => setAction(null)} {...operation} protectChanges hasChanges={Boolean(note)}>
      <p>{action === "cancel" ? "保留已经签署的审批历史，停止这次人员变动。" : "保留原批准内容，重新检查当前条件；不会覆盖期间已经修改的人员事实。"}</p>
      <form onSubmit={event => { event.preventDefault(); const body = officeCommand(client, `${row.id}:personnel:${action}`, { commandKey: undefined, expectedVersion: row.versionNumber, note }); void operation.run(signal => action === "cancel" ? client.cancelApprovedPersonnelChange({ id: row.id, body }, { signal }) : client.executeApprovedPersonnelChange({ id: row.id, body }, { signal }), () => setAction(null)); }}>
        <OfficeField label="处理说明"><textarea required maxLength={500} value={note} onChange={event => setNote(event.target.value)} /></OfficeField>
        <OfficeSubmit busy={operation.busy} label={label} />
      </form>
    </OfficeDialog>}
  </>;
}
