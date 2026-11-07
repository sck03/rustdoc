import type { ExportDocManagerApiClient,OaApprovalStage,OaApprovalRule,OaRoutingUnit } from "../../api/index.ts";
import { useState } from "react";
import type { ApprovalAccount } from "./useApprovalSettings.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { ApprovalRevisionPolicyEditor } from "./ApprovalRevisionPolicyEditor.tsx";
import { ApprovalMaterialPolicyEditor } from "./ApprovalMaterialPolicyEditor.tsx";
import { ApprovalSourcePicker,approvalSources } from "./ApprovalSourcePicker.tsx";
import { WorkflowNodeSettings } from "./WorkflowNodeSettings.tsx";
const initialStage=(dependsOn:string[]=[]):OaApprovalStage=>({key:createRequestKey(),name:"审批节点",kind:"Approval",phase:"Decision",dependsOn,source:"DepartmentManager",approverUserIds:[],decision:"All",selfApproval:"Skip",repeatedApprover:"Allow",conditions:[],dueHours:48});
export type ApprovalStagesEditorProps={client:ExportDocManagerApiClient;stages:OaApprovalStage[];accounts:ApprovalAccount[];handlers:ApprovalAccount[];units:OaRoutingUnit[];currentCompanyCode:string;definitions:OaApprovalRule[];onChange:(stages:OaApprovalStage[])=>void};
export function ApprovalStagesEditor({client,stages,accounts,handlers,units,currentCompanyCode,definitions,onChange}:ApprovalStagesEditorProps) {
  const [catalogUnits,setCatalogUnits]=useState<Record<string,number>>({});
  const ancestorIds=new Set(units.find(unit=>unit.code===currentCompanyCode)?.ancestorCompanyIds??[]);
  const patch=(index:number,change:Partial<OaApprovalStage>)=>onChange(stages.map((old,i)=>i===index?{...old,...change}:old));
  return <div className="oa-stages-editor"><p className="office-muted">无前置节点的节点同时开始，多个前置须全部完成。获批办理仅在批准后激活；编辑后须用实际人员样例模拟并发布。</p>
    {stages.map((stage,index)=>{
      const kind=stage.kind??"Approval",hasActors=["Approval","Handling","Copy"].includes(kind),approval=kind==="Approval";
      const sourceAccounts=kind==="Handling"?handlers:accounts;
      const routes=["CompanyExecutive","CompanyLeaders","Position","Responsibility"].includes(stage.source);
      const ancestorCatalog=stage.organizationRoute?.mode==="Ancestor"&&["Position","Responsibility"].includes(stage.source);
      const unitId=ancestorCatalog?catalogUnits[stage.key]:stage.organizationRoute?.unitId??units.find(u=>u.code===currentCompanyCode)?.id;
      return <fieldset className="office-resource-card" key={stage.key}><legend>{index+1} · {stage.name}</legend>
        <OfficeField label="节点名称"><input required maxLength={100} value={stage.name} onChange={e=>patch(index,{name:e.target.value})}/></OfficeField>
        <WorkflowNodeSettings client={client} stage={stage} stages={stages} onChange={change=>patch(index,change)} accounts={accounts} units={units} currentCompanyCode={currentCompanyCode} definitions={definitions}/>
        <div className="office-form-grid">
          {hasActors&&<><OfficeField label="责任人来源"><select value={stage.source} onChange={e=>patch(index,{source:e.target.value as OaApprovalStage["source"],sourceId:null,approverUserIds:[],organizationRoute:{mode:"Current",unitId:null}})}>{Object.entries(approvalSources).filter(([v])=>!approval||v!=="Requester"&&v!=="Handlers").map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></OfficeField>
            {routes&&<><OfficeField label="来源所属单位"><select value={stage.organizationRoute?.mode??"Current"} onChange={e=>patch(index,{sourceId:null,organizationRoute:{mode:e.target.value as "Current"|"Specified"|"Ancestor",unitId:null}})}><option value="Current">申请所在单位</option><option value="Specified">指定经营单位</option><option value="Ancestor">沿上级单位查找</option></select></OfficeField>
              {stage.organizationRoute?.mode==="Specified"&&<OfficeField label="管理单位"><select required value={stage.organizationRoute.unitId??""} onChange={e=>patch(index,{sourceId:null,organizationRoute:{mode:"Specified",unitId:Number(e.target.value)}})}><option value="">选择经营单位</option>{units.map(unit=><option key={unit.id} value={unit.id}>{unit.name} · {unit.code}</option>)}</select></OfficeField>}
              {ancestorCatalog&&<OfficeField label="上级职责目录"><select value={unitId??""} onChange={event=>{setCatalogUnits(current=>({...current,[stage.key]:Number(event.target.value)}));patch(index,{sourceId:null});}}><option value="">选择职责所在上级单位</option>{units.filter(unit=>ancestorIds.has(unit.id)).map(unit=><option key={unit.id} value={unit.id}>{unit.name} · {unit.code}</option>)}</select></OfficeField>}
              <p className="office-field-wide office-muted">跨单位责任人仍须取得申请单位的对应业务授权；职责本身不授予权限。</p></>}
            {(!ancestorCatalog||Boolean(unitId))&&<ApprovalSourcePicker client={client} source={stage.source} sourceId={stage.sourceId} unitId={unitId} onChange={sourceId=>patch(index,{sourceId})}/>}
          </>}
          {approval&&<><OfficeField label="通过方式"><select value={stage.decision} onChange={e=>patch(index,{decision:e.target.value as OaApprovalStage["decision"],approvalPercentage:100})}><option value="All">会签：全部同意</option><option value="Any">或签：任一人同意</option><option value="Ratio">比例会签</option></select></OfficeField>
            {stage.decision==="Ratio"&&<OfficeField label="同意比例（%，人数向上取整）"><input required type="number" min={1} max={100} step={1} value={stage.approvalPercentage??100} onChange={e=>patch(index,{approvalPercentage:Number(e.target.value)})}/></OfficeField>}
            <OfficeField label="出现不同意意见"><select value={stage.rejectionPolicy??"Immediate"} onChange={e=>patch(index,{rejectionPolicy:e.target.value as "Immediate"|"Collect"})}><option value="Immediate">立即否决</option><option value="Collect">继续收集，按票数决定</option></select></OfficeField>
            <OfficeField label="遇到申请人本人"><select value={stage.selfApproval} onChange={e=>patch(index,{selfApproval:e.target.value as OaApprovalStage["selfApproval"]})}><option value="Skip">回避本人</option><option value="Reject">停止提交，补充责任人</option></select></OfficeField></>}
          {hasActors&&<OfficeField label="独立复核"><select value={stage.repeatedApprover} onChange={e=>patch(index,{repeatedApprover:e.target.value as OaApprovalStage["repeatedApprover"]})}><option value="Allow">允许兼任，逐节点记录</option><option value="Reject">要求独立自然人</option></select></OfficeField>}
          <OfficeField label="办理时限（小时）"><input required type="number" min={1} max={8760} step={1} value={stage.dueHours} onChange={e=>patch(index,{dueHours:Number(e.target.value)})}/></OfficeField><OfficeField label="时限计算"><select value={stage.deadlineMode??"Natural"} onChange={e=>patch(index,{deadlineMode:e.target.value as "Natural"|"Working"})}><option value="Natural">自然时长</option><option value="Working">公司工作时长</option></select></OfficeField>
        </div>
        <details><summary>审查要点（{stage.reviewPoints?.length??0}）</summary>{(stage.reviewPoints??[]).map((point,i)=><div className="office-card-actions" key={i}><input required aria-label={`审批要点 ${i+1}`} maxLength={200} value={point} onChange={e=>patch(index,{reviewPoints:stage.reviewPoints?.map((p,j)=>i===j?e.target.value:p)})}/><button type="button" onClick={()=>patch(index,{reviewPoints:stage.reviewPoints?.filter((_,j)=>i!==j)})}>移除</button></div>)}<button type="button" disabled={(stage.reviewPoints?.length??0)>=12} onClick={()=>patch(index,{reviewPoints:[...(stage.reviewPoints??[]),""]})}>添加审批要点</button></details>
        <details><summary>前置节点（{stage.dependsOn.length}）</summary>{stages.filter(n=>n.key!==stage.key&&((stage.phase??"Decision")==="Fulfillment"||(n.phase??"Decision")==="Decision")).map(node=><label className="checkbox-field" key={node.key}><input type="checkbox" checked={stage.dependsOn.includes(node.key)} onChange={e=>patch(index,{dependsOn:e.target.checked?[...stage.dependsOn,node.key]:stage.dependsOn.filter(k=>k!==node.key)})}/>{node.name}</label>)}</details>
        {hasActors&&stage.source==="Named"&&<OfficeField label="责任账号（可多选）"><select multiple required value={stage.approverUserIds.map(String)} onChange={e=>patch(index,{approverUserIds:Array.from(e.target.selectedOptions,o=>Number(o.value))})}>{sourceAccounts.map(a=><option key={a.id} value={a.id}>{a.fullName} · {a.username}</option>)}</select></OfficeField>}
        {hasActors&&<ApprovalMaterialPolicyEditor value={stage.materialPolicy} onChange={materialPolicy=>patch(index,{materialPolicy})}/>}
        {approval&&<ApprovalRevisionPolicyEditor value={stage.revisionPolicy} accounts={accounts} onChange={revisionPolicy=>patch(index,{revisionPolicy})}/>}
        <button type="button" className="command-button secondary" disabled={stages.length===1||stages.some(n=>n.dependsOn.includes(stage.key)||(n.branches??[]).some(b=>b.target===stage.key))} onClick={()=>onChange(stages.filter((_,i)=>i!==index))}>移除节点</button>
      </fieldset>;
    })}
    <button type="button" className="command-button secondary" disabled={stages.length>=32} onClick={()=>onChange([...stages,initialStage(stages.length?[stages[stages.length-1].key]:[])])}>添加流程节点</button>
  </div>;
}
