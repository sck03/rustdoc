import type { OaApprovalStage, OaApprovalCondition, OaRoutingUnit } from "../../api/index.ts";
import type { ApprovalAccount } from "./useApprovalSettings.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { ApprovalRevisionPolicyEditor } from "./ApprovalRevisionPolicyEditor.tsx";
import { ApprovalMaterialPolicyEditor } from "./ApprovalMaterialPolicyEditor.tsx";

const sources: Record<OaApprovalStage["source"], string> = { Named: "指定人员", DepartmentManager: "任职部门主负责人", DepartmentLeaders: "部门主负责人与分管负责人", LineManager: "直属负责人", FunctionalManagers: "职能负责人", CompanyExecutive: "公司主负责人", CompanyLeaders: "公司主负责人与分管负责人" };
const fields: Record<OaApprovalCondition["field"],string>={Amount:"原币金额",Currency:"币种",Duration:"时长（请假／出差为天，加班为小时）",Department:"部门编号",Category:"业务类别",PersonnelAction:"人员变动类型"};
const operators:Record<OaApprovalCondition["operator"],string>={Equal:"等于",NotEqual:"不等于",Greater:"大于",GreaterOrEqual:"大于等于",Less:"小于",LessOrEqual:"小于等于"};
export const initialStage = (dependsOn:string[]=[]): OaApprovalStage => ({ key:createRequestKey(), name:"审批节点",dependsOn, source:"DepartmentManager",approverUserIds:[],decision:"All",selfApproval:"Skip",repeatedApprover:"Allow",conditions:[],dueHours:48 });

export function ApprovalStagesEditor({ stages, accounts, units, onChange }: { stages: OaApprovalStage[]; accounts: ApprovalAccount[]; units: OaRoutingUnit[]; onChange: (stages: OaApprovalStage[]) => void }) {
  const patch=(index:number,change:Partial<OaApprovalStage>)=>onChange(stages.map((old,i)=>i===index?{...old,...change}:old));
  return <div className="oa-stages-editor">
    <p className="office-muted">无前置节点的审批同时开始；选择多个前置节点时，须全部结束才进入本节点。条件须全部满足，未满足时跳过节点。发布时检查循环、责任人和条件；提交前可预览实际路线。</p>
    {stages.map((stage,index)=><fieldset className="office-resource-card" key={stage.key}><legend>{index+1} · {stage.name}</legend>
      <div className="office-form-grid">
        <OfficeField label="节点名称"><input required maxLength={100} value={stage.name} onChange={e=>patch(index,{name:e.target.value})}/></OfficeField>
        <OfficeField label="审批人来源"><select value={stage.source} onChange={e=>patch(index,{source:e.target.value as OaApprovalStage["source"],approverUserIds:[],organizationRoute:{mode:"Current",unitId:null}})}>{Object.entries(sources).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></OfficeField>
        {(stage.source === "CompanyExecutive" || stage.source === "CompanyLeaders") && <>
          <OfficeField label="负责人所属单位"><select value={stage.organizationRoute?.mode ?? "Current"} onChange={e => patch(index, { organizationRoute: { mode: e.target.value as "Current" | "Specified" | "Ancestor", unitId: null } })}><option value="Current">申请所在单位</option><option value="Specified">指定管理单位</option><option value="Ancestor">沿上级寻找已配置负责人</option></select></OfficeField>
          {stage.organizationRoute?.mode === "Specified" && <OfficeField label="管理单位"><select required value={stage.organizationRoute.unitId ?? ""} onChange={e => patch(index, { organizationRoute: { mode: "Specified", unitId: Number(e.target.value) } })}><option value="">选择经营单位</option>{units.map(unit => <option key={unit.id} value={unit.id}>{unit.name} · {unit.code}</option>)}</select></OfficeField>}
          <p className="office-field-wide office-muted">跨单位负责人仍须取得申请单位的对应审批授权。集团目录不承载业务职责。</p>
        </>}
        <OfficeField label="通过方式"><select value={stage.decision} onChange={e=>patch(index,{decision:e.target.value as OaApprovalStage["decision"],approvalPercentage:100})}><option value="All">会签：全部同意</option><option value="Any">或签：任一人同意</option><option value="Ratio">比例会签</option></select></OfficeField>
        {stage.decision === "Ratio" && <OfficeField label="同意比例（%，人数向上取整）"><input type="number" min={1} max={100} step={1} required value={stage.approvalPercentage ?? 100} onChange={e=>patch(index,{approvalPercentage:Number(e.target.value)})}/></OfficeField>}
        <OfficeField label="出现不同意意见"><select value={stage.rejectionPolicy ?? "Immediate"} onChange={e=>patch(index,{rejectionPolicy:e.target.value as "Immediate" | "Collect"})}><option value="Immediate">立即否决</option><option value="Collect">继续收集，按通过票数决定</option></select></OfficeField>
        <OfficeField label="办理时限（小时）"><input type="number" min={1} max={8760} required value={stage.dueHours} onChange={e=>patch(index,{dueHours:Number(e.target.value)})}/></OfficeField>
        <OfficeField label="时限计算"><select value={stage.deadlineMode??"Natural"} onChange={e=>patch(index,{deadlineMode:e.target.value as "Natural"|"Working"})}><option value="Natural">自然时长</option><option value="Working">公司工作时长</option></select></OfficeField>
        <OfficeField label="遇到申请人本人"><select value={stage.selfApproval} onChange={e=>patch(index,{selfApproval:e.target.value as OaApprovalStage["selfApproval"]})}><option value="Skip">回避本人</option><option value="Reject">停止提交，补充责任人</option></select></OfficeField>
        <OfficeField label="人员兼任"><select value={stage.repeatedApprover} onChange={e=>patch(index,{repeatedApprover:e.target.value as OaApprovalStage["repeatedApprover"]})}><option value="Allow">允许兼任，逐节点签署</option><option value="Reject">要求独立复核人</option></select></OfficeField>
      </div>
      <details><summary>审批要点（{stage.reviewPoints?.length ?? 0}）</summary><p>随发布版本保存，并在审批时显示。</p>{(stage.reviewPoints ?? []).map((point,i) => <div className="office-card-actions" key={i}><input aria-label={`审批要点 ${i+1}`} required maxLength={200} value={point} onChange={e => patch(index,{reviewPoints:stage.reviewPoints?.map((p,j) => j === i ? e.target.value : p)})}/><button type="button" onClick={() => patch(index,{reviewPoints:stage.reviewPoints?.filter((_,j)=>j!==i)})}>移除</button></div>)}<button type="button" disabled={(stage.reviewPoints?.length ?? 0) >= 12} onClick={() => patch(index,{reviewPoints:[...(stage.reviewPoints??[]),""]})}>添加审批要点</button></details>
      <details><summary>前置节点（{stage.dependsOn.length}）</summary>{stages.filter(n=>n.key!==stage.key).map(node=><label className="checkbox-field" key={node.key}><input type="checkbox" checked={stage.dependsOn.includes(node.key)} onChange={e=>patch(index,{dependsOn:e.target.checked?[...stage.dependsOn,node.key]:stage.dependsOn.filter(k=>k!==node.key)})}/>{node.name}</label>)}</details>
      {stage.source==="Named"&&<OfficeField label="审批账号（可多选）"><select multiple value={stage.approverUserIds.map(String)} onChange={e=>patch(index,{approverUserIds:Array.from(e.target.selectedOptions,o=>Number(o.value))})}>{accounts.map(a=><option key={a.id} value={a.id}>{a.fullName} · {a.username}</option>)}</select></OfficeField>}
      <details><summary>生效条件（{stage.conditions.length}）</summary><p className="office-muted">金额条件必须同时限定币种，不自动换汇。类别和部门填写业务编号。</p>
        {stage.conditions.map((c,i)=><div className="office-form-grid" key={i}>
          <OfficeField label="条件字段"><select value={c.field} onChange={e=>patch(index,{conditions:stage.conditions.map((v,j)=>i===j?{...v,field:e.target.value as OaApprovalCondition["field"]}:v)})}>{Object.entries(fields).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></OfficeField>
          <OfficeField label="比较方式"><select value={c.operator} onChange={e=>patch(index,{conditions:stage.conditions.map((v,j)=>i===j?{...v,operator:e.target.value as OaApprovalCondition["operator"]}:v)})}>{Object.entries(operators).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></OfficeField>
          <OfficeField label="条件值"><input required value={c.value} onChange={e=>patch(index,{conditions:stage.conditions.map((v,j)=>i===j?{...v,value:e.target.value}:v)})}/></OfficeField>
          <button type="button" className="command-button secondary" onClick={()=>patch(index,{conditions:stage.conditions.filter((_,j)=>i!==j)})}>移除条件</button>
        </div>)}
        <button type="button" className="command-button secondary" disabled={stage.conditions.length>=16} onClick={()=>patch(index,{conditions:[...stage.conditions,{field:"Department",operator:"Equal",value:""}]})}>添加条件</button>
      </details>
      <ApprovalMaterialPolicyEditor value={stage.materialPolicy} onChange={materialPolicy=>patch(index,{materialPolicy})}/><ApprovalRevisionPolicyEditor value={stage.revisionPolicy} accounts={accounts} onChange={revisionPolicy=>patch(index,{revisionPolicy})}/>
      <button type="button" className="command-button secondary" disabled={stages.length===1||stages.some(n=>n.dependsOn.includes(stage.key))} onClick={()=>onChange(stages.filter((_,i)=>i!==index))}>移除节点</button>
    </fieldset>)}
    <button type="button" className="command-button secondary" disabled={stages.length>=32} onClick={()=>onChange([...stages,initialStage(stages.length?[stages[stages.length-1].key]:[])])}>添加审批节点</button>
  </div>;
}
