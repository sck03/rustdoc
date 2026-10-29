import { useState } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, OaApprovalRule, OaSimulationResult } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { OrganizationManagerPicker } from "../organization/OrganizationManagerPicker.tsx";
import { OaApprovalSteps } from "./OaApprovalSteps.tsx";

export function ApprovalSimulationDialog({client,user,rule,onClose}:{client:ExportDocManagerApiClient;user:ApiUserDto;rule:OaApprovalRule;onClose:()=>void}) {
  const operation=useOfficeOperation();
  const [choosing,setChoosing]=useState(false);
  const [applicant,setApplicant]=useState<{id:number;appointmentId:string;name:string}|null>(null);
  const [amount,setAmount]=useState("0");
  const [currency,setCurrency]=useState("CNY");
  const [duration,setDuration]=useState("1");
  const [category,setCategory]=useState("");
  const [result,setResult]=useState<OaSimulationResult|null>(null);
  return <><OfficeDialog title={`模拟流程 · ${rule.name}`} onClose={onClose} {...operation}>
    <p>按所选人员任职及当前权限解析草稿。模拟不会创建申请、审批任务或通知；正式提交时重新核验。</p>
    {rule.kind === "personnel" && rule.variant === "Hire" && <p>此处借用现有任职验证入职流程的组织路线。拟入职人员的汇报关系须在实际入职申请中再次预览。</p>}
    <form onSubmit={event=>{event.preventDefault();if(!applicant)return;void operation.run(signal=>client.simulateOaDefinition({body:{definition:rule,employeeId:applicant.id,appointmentId:applicant.appointmentId,amount,currency,duration,category}},{signal}),setResult);}}>
      <fieldset disabled={operation.busy} className="office-form-grid">
        <div className="office-field-wide"><strong>模拟申请任职：{applicant?.name ?? "尚未选择"}</strong><button type="button" className="command-button secondary" onClick={()=>setChoosing(true)}>选择人员任职</button></div>
        {(rule.kind==="expense"||rule.kind==="purchase")&&<><OfficeField label="原币金额"><input required inputMode="decimal" value={amount} onChange={e=>{setAmount(e.target.value);setResult(null);}} /></OfficeField><OfficeField label="币种"><select value={currency} onChange={e=>{setCurrency(e.target.value);setResult(null);}}>{["CNY","USD","EUR","HKD","JPY","GBP"].map(code=><option key={code}>{code}</option>)}</select></OfficeField></>}
        {["leave","overtime","travel"].includes(rule.kind)&&<OfficeField label={rule.kind==="overtime"?"时长（小时）":"时长（天）"}><input required inputMode="decimal" value={duration} onChange={e=>{setDuration(e.target.value);setResult(null);}} /></OfficeField>}
        {(rule.kind==="leave"||rule.kind==="general")&&<OfficeField label="类别代码"><input value={category} onChange={e=>{setCategory(e.target.value);setResult(null);}} /></OfficeField>}
      </fieldset><OfficeSubmit busy={operation.busy || !applicant} label="解析模拟路线" />
    </form>
    {result&&<OaApprovalSteps plan={result.approvalPlan} notes={result.notes} user={user}/>}
  </OfficeDialog>{choosing&&<OrganizationManagerPicker client={client} companyCode={user.companyScope} onClose={()=>setChoosing(false)} onSelect={person=>{setApplicant({id:person.id,appointmentId:person.appointmentId,name:`${person.fullName} · ${person.departmentName}`});setResult(null);setChoosing(false);}}/>}</>;
}
