import { useState } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient, OaRequest, OaWorkflowCommandSave } from "../../api/index.ts";
import type { OfficeRequestRow } from "../office/officeModel.ts";
import { officeRequestApi } from "../office/officeRequestApi.ts";
import { oaApi } from "./oaApi.ts";
import { OfficeDialog,OfficeField,OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { officeCommand } from "../office/officeCommands.ts";
import { workflowTargets } from "./workflowOperationModel.ts";
import { officeWorkflowQueryRoots } from "../../api/queryKeys.ts";
type Command=OaWorkflowCommandSave["command"];
const labels:Record<Command,string>={AddBefore:"前加签",AddAfter:"后加签",AddParallel:"并行加签",RemoveSigner:"减签",ReturnToNode:"退回指定节点",Suspend:"暂停流程",Resume:"恢复流程",Terminate:"终止流程",RetryNode:"重试失败系统动作",CancelSubflow:"取消子流程",RetrySubflow:"重试失败子流程",CompensateSubflow:"登记子流程补偿"};
export function WorkflowOperations({client,row,disabled=false}:{client:ExportDocManagerApiClient;row:OaRequest|OfficeRequestRow;disabled?:boolean}) {
  const [opened,setOpened]=useState(false);
  if(!row.workflowActions?.length)return null;
  return <><button type="button" className="command-button secondary" disabled={disabled} onClick={()=>setOpened(true)}>流程操作</button>{opened&&<WorkflowOperationDialog client={client} row={row} onClose={()=>setOpened(false)}/>}</>;
}
function WorkflowOperationDialog({client,row,onClose}:{client:ExportDocManagerApiClient;row:OaRequest|OfficeRequestRow;onClose:()=>void}) {
  const kind="meetingRoomId" in row?"room":"officeSupplyId" in row?"supply":row.kind;
  const operation=useOfficeOperation(officeWorkflowQueryRoots(kind),false);
  const [command,setCommand]=useState<Command>(row.workflowActions![0]);
  const [target,setTarget]=useState("");
  const [users,setUsers]=useState<number[]>([]);
  const [note,setNote]=useState("");
  const [subflowKey,setSubflowKey]=useState("");
  const [quantity,setQuantity]=useState(1);
  const childCommand=command.endsWith("Subflow");
  const subflows=row.approvalPlan?.subflows??[];
  const child=subflows.find(s=>s.key===subflowKey)??subflows[0];
  const choosing=["AddBefore","AddAfter","AddParallel","RemoveSigner","ReturnToNode","RetryNode"].includes(command);
  const adding=command.startsWith("Add");
  const api="meetingRoomId" in row?officeRequestApi(client,"rooms"):"officeSupplyId" in row?officeRequestApi(client,"supplies"):oaApi(client,row.kind);
  const accounts=useInfiniteQuery({queryKey:["office","workflow-candidates",row.companyScope,row.id,row.versionNumber],initialPageParam:undefined as number|undefined,queryFn:({signal,pageParam})=>api.workflowCandidates(row.id,pageParam,{signal}),getNextPageParam:page=>page.nextCursor??undefined,enabled:adding});
  const nodes=workflowTargets(row.approvalPlan,row.reviewTaskKeys,row.workflowActions??[],command);
  const selected=nodes.find(n=>n.key===target)??nodes[0];
  const removable=row.approvalPlan?.steps.filter(s=>row.approvalPlan?.nodes[s.stage].key===selected?.key&&s.status==="Waiting"&&!s.mandatory&&!selected?.revisionPolicy?.protectedUserIds?.includes(s.approverUserId))??[];
  return <OfficeDialog title="流程操作" onClose={onClose} {...operation} protectChanges hasChanges={Boolean(note||users.length)}>
    <form onSubmit={e=>{e.preventDefault();const body=officeCommand(client,`${row.id}:workflow:${command}`,{commandKey:undefined,expectedVersion:row.versionNumber,command,note,...(choosing?{targetNodeKey:selected?.key,userIds:users}:{}),...(childCommand?{subflowKey:child?.key,quantity}:{})});void operation.run<OaRequest|OfficeRequestRow>(async signal=>await api.workflow(row.id,body,{signal}),onClose);}}>
      <fieldset disabled={operation.busy} className="office-form-grid">
        <OfficeField label="操作"><select value={command} onChange={e=>{setCommand(e.target.value as Command);setTarget("");setUsers([]);}}>{row.workflowActions?.map(action=><option key={action} value={action}>{labels[action]}</option>)}</select></OfficeField>
        {choosing&&<OfficeField label="目标审批节点"><select required value={selected?.key??""} onChange={e=>{setTarget(e.target.value);setUsers([]);}}>{nodes.map(node=><option key={node.key} value={node.key}>{node.name}</option>)}</select></OfficeField>}
        {childCommand&&<><OfficeField label="目标子流程"><select required value={child?.key??""} onChange={e=>setSubflowKey(e.target.value)}>{subflows.map(flow=><option key={flow.key} value={flow.key}>{flow.definitionKey} · 版本 {flow.version} · {row.approvalPlan?.subflowProgress?.[flow.key]?.outcome}</option>)}</select></OfficeField>{command==="CompensateSubflow"&&child?.policy?.compensation==="ReturnResource"&&<OfficeField label="实际归还数量"><input type="number" required min={1} max={1000000} value={quantity} onChange={e=>setQuantity(Number(e.target.value))}/></OfficeField>}<p className="office-muted office-field-wide">补偿须如实登记已完成的现实动作；重试保留旧签署并建立新的执行代次。</p></>}
        {(adding||command==="RemoveSigner")&&<OfficeField label={adding?"新增审批人":"减去未签署人员"}><select multiple required value={users.map(String)} onChange={e=>setUsers(Array.from(e.target.selectedOptions,o=>Number(o.value)))}>{adding?accounts.data?.pages.flatMap(page=>page.users).map(a=><option key={a.id} value={a.id}>{a.fullName} · #{a.id}</option>):removable.map(a=><option key={a.taskKey} value={a.approverUserId}>{a.approverName}</option>)}</select></OfficeField>}
        {adding&&accounts.hasNextPage&&<button type="button" className="command-button secondary" disabled={accounts.isFetchingNextPage} onClick={()=>void accounts.fetchNextPage()}>加载更多候选人</button>}
        {accounts.isError&&<InlineNotice tone="error">{readApiError(accounts.error)} <button type="button" onClick={()=>void accounts.refetch()}>重试人员查询</button></InlineNotice>}
        <OfficeField label="操作原因" wide><textarea required maxLength={500} value={note} onChange={e=>setNote(e.target.value)}/></OfficeField>
      </fieldset><OfficeSubmit busy={operation.busy} label={labels[command]} disabled={choosing&&!selected || childCommand&&!child || (adding||command==="RemoveSigner")&&!users.length}/>
    </form>
  </OfficeDialog>;
}
