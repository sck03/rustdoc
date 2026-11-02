import { useState } from "react";
import type { ApiUserDto,ExportDocManagerApiClient,OaRequest,OaFulfillmentTask } from "../../api/index.ts";
import type { OfficeRequestRow } from "../office/officeModel.ts";
import { oaApi } from "./oaApi.ts";
import { officeRequestApi } from "../office/officeRequestApi.ts";
import { OfficeDialog,OfficeField,OfficeSubmit } from "../office/OfficeUi.tsx";
import { ApprovalVerificationFields } from "./ApprovalVerificationFields.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { officeWorkflowQueryRoots } from "../../api/queryKeys.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
const states:Record<string,string>={Pending:"等待前置",Active:"待办理",Completed:"已完成",Blocked:"等待处理",Suspended:"已暂停",Cancelled:"已关闭"};
export function WorkflowFulfillmentPanel({client,user,row}:{client:ExportDocManagerApiClient;user:ApiUserDto;row:OaRequest|OfficeRequestRow}) {
  const [selected,setSelected]=useState<OaFulfillmentTask|null>(null);
  if(!row.workflowTasks?.length)return null;
  return <section className="oa-detail-content" aria-label="获批办理进度"><h3>获批办理 · {states[row.fulfillmentState??"Pending"]}</h3><p className="office-muted">审批决定、实际交付和技术输出分别记录。归还、财务接收及验收须据实登记。</p><ol className="oa-history">{row.workflowTasks.map(task=><li key={task.nodeKey}><strong>{task.name}</strong> · {states[task.state]}{task.errorCode&&<p>待处理：{task.errorCode}</p>}{task.dueAt&&<p>期限：{formatBusinessDateTime(task.dueAt,user.businessTimeZone)}</p>}{task.executedAt&&<p>最近办理：{formatBusinessDateTime(task.executedAt,user.businessTimeZone)}</p>}{task.canExecute&&task.action==="complete"&&<button type="button" className="command-button" onClick={()=>setSelected(task)}>办理此节点</button>}{task.canExecute&&task.action!=="complete"&&<p>使用本申请的交接／归还操作登记实物结果。</p>}</li>)}</ol>{selected&&<FulfillmentDialog client={client} row={row} task={selected} onClose={()=>setSelected(null)}/>}</section>;
}
function FulfillmentDialog({client,row,task,onClose}:{client:ExportDocManagerApiClient;row:OaRequest|OfficeRequestRow;task:OaFulfillmentTask;onClose:()=>void}) {
  const kind="meetingRoomId" in row?"room":"officeSupplyId" in row?"supply":row.kind;
  const api="meetingRoomId" in row?officeRequestApi(client,"rooms"):"officeSupplyId" in row?officeRequestApi(client,"supplies"):oaApi(client,row.kind);
  const operation=useOfficeOperation(officeWorkflowQueryRoots(kind),false);
  const [decision,setDecision]=useState<"Approve"|"Reject"|"Return">("Approve");
  const [note,setNote]=useState("");
  const [values,setValues]=useState<Record<string,string|boolean>>({});
  const [points,setPoints]=useState<number[]>([]);
  return <OfficeDialog title={task.name} onClose={onClose} {...operation} protectChanges hasChanges={Boolean(note||Object.keys(values).length||points.length)}><form onSubmit={e=>{e.preventDefault();void operation.run<OaRequest|OfficeRequestRow>(signal=>api.fulfillment(row.id,{expectedVersion:row.versionNumber,nodeKey:task.nodeKey,taskKey:task.taskKey??undefined,note,decision,verificationValues:values,confirmedReviewPoints:points},{signal}),onClose);}}><fieldset disabled={operation.busy}>
    {task.kind==="Approval"&&<OfficeField label="办理核验决定"><select value={decision} onChange={e=>setDecision(e.target.value as typeof decision)}><option value="Approve">核验通过</option><option value="Reject">核验不通过</option><option value="Return">退回本次办理</option></select></OfficeField>}
    <ApprovalVerificationFields node={task} values={values} points={points} onValues={setValues} onPoints={setPoints} requireCompletion={decision==="Approve"}/>
    <OfficeField label="实际办理说明"><textarea required rows={4} maxLength={500} value={note} onChange={e=>setNote(e.target.value)}/></OfficeField>
  </fieldset><OfficeSubmit busy={operation.busy} label="保存办理结果"/></form></OfficeDialog>;
}
