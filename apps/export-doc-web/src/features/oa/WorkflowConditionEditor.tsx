import type { OaApprovalCondition, OaConditionGroup } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
const fields: Record<OaApprovalCondition["field"], string> = { Amount:"原币金额",Currency:"币种",Duration:"时长",Department:"部门编号",Category:"业务类别",PersonnelAction:"人员变动类型",Returnable:"是否借用归还" };
const operators: Record<OaApprovalCondition["operator"], string> = { Equal:"等于",NotEqual:"不等于",Greater:"大于",GreaterOrEqual:"大于等于",Less:"小于",LessOrEqual:"小于等于" };
export function conditionCount(group: OaConditionGroup): number { return (group.conditions?.length ?? 0) + (group.groups ?? []).reduce((n,g)=>n+conditionCount(g),0); }
export function WorkflowConditionEditor({value,onChange,depth=1,available=16}:{value:OaConditionGroup;onChange:(value:OaConditionGroup)=>void;depth?:number;available?:number}) {
  const conditions=value.conditions ?? [], groups=value.groups ?? [];
  const patch=(index:number,change:Partial<OaApprovalCondition>)=>onChange({...value,conditions:conditions.map((c,i)=>i===index?{...c,...change}:c)});
  return <div className="oa-condition-group">
    <OfficeField label="条件组合"><select value={value.logic ?? "All"} onChange={e=>onChange({...value,logic:e.target.value as "All"|"Any"})}><option value="All">全部满足（AND）</option><option value="Any">任一满足（OR）</option></select></OfficeField>
    {conditions.map((condition,i)=><div className="office-form-grid" key={i}>
      <OfficeField label="条件字段"><select value={condition.field} onChange={e=>patch(i,{field:e.target.value as OaApprovalCondition["field"]})}>{Object.entries(fields).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></OfficeField>
      <OfficeField label="比较方式"><select value={condition.operator} onChange={e=>patch(i,{operator:e.target.value as OaApprovalCondition["operator"]})}>{Object.entries(operators).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></OfficeField>
      <OfficeField label="条件值">{condition.field==="Returnable"?<select required value={condition.value} onChange={e=>patch(i,{value:e.target.value})}><option value="">请选择</option><option value="true">借用</option><option value="false">消耗品</option></select>:<input required maxLength={100} value={condition.value} onChange={e=>patch(i,{value:e.target.value})}/>}</OfficeField>
      <button type="button" onClick={()=>onChange({...value,conditions:conditions.filter((_,j)=>i!==j)})}>移除条件</button>
    </div>)}
    {groups.map((group,i)=><fieldset className="office-resource-card" key={i}><legend>条件子组 {i+1}</legend><WorkflowConditionEditor value={group} depth={depth+1} available={available} onChange={change=>onChange({...value,groups:groups.map((g,j)=>i===j?change:g)})}/><button type="button" onClick={()=>onChange({...value,groups:groups.filter((_,j)=>i!==j)})}>移除子组</button></fieldset>)}
    <div className="office-card-actions"><button type="button" disabled={available<=0 || conditions.length+groups.length>=16} onClick={()=>onChange({...value,conditions:[...conditions,{field:"Department",operator:"Equal",value:""}]})}>添加条件</button><button type="button" disabled={depth>=4 || available<=0 || conditions.length+groups.length>=16} onClick={()=>onChange({...value,groups:[...groups,{logic:"All",conditions:[{field:"Department",operator:"Equal",value:""}],groups:[]}]})}>添加条件子组</button></div>
  </div>;
}
