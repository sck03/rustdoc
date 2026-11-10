import { Button } from "../../ui/Button.tsx";
import { useState } from "react";
import type { ApiUserDto,ExportDocManagerApiClient,OaApprovalRule,OaSimulationResult,OaSimulationSample } from "../../api/index.ts";
import { OfficeDialog,OfficeField,OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { OrganizationManagerPicker } from "../organization/OrganizationManagerPicker.tsx";
import { OaApprovalSteps } from "./OaApprovalSteps.tsx";
import { businessDateTimeLocalInputToIso } from "../../ui/businessTime.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { emptyOrganization, ReportingEditor } from "../office/PersonnelOrganizationEditor.tsx";
import { WorkflowProjectPicker } from "./WorkflowProjectPicker.tsx";
import { OaFormFields } from "./OaFormFields.tsx";
export function ApprovalSimulationDialog({client,user,rule,onClose,onPublish,busy=false,error=""}:{client:ExportDocManagerApiClient;user:ApiUserDto;rule:OaApprovalRule;onClose:()=>void;onPublish?:(samples:OaSimulationSample[],activateAt?:string)=>Promise<void>;busy?:boolean;error?:string}) {
  const operation=useOfficeOperation([],false);
  const [choosing,setChoosing]=useState(false);
  const [applicant,setApplicant]=useState<{id:number;appointmentId:string;name:string}|null>(null);
  const [amount,setAmount]=useState("0"),[currency,setCurrency]=useState("CNY"),[duration,setDuration]=useState("1"),[category,setCategory]=useState("");
  const [isReturnable,setReturnable]=useState(true);
  const [formValues,setFormValues]=useState<import("../../api/index.ts").OaFormValues>({});
  const hiring=rule.kind==="personnel"&&rule.variant==="Hire";
  const [reporting,setReporting]=useState(emptyOrganization);
  const [projectId,setProjectId]=useState<string|null>(null);
  const [result,setResult]=useState<OaSimulationResult|null>(null);
  const [samples,setSamples]=useState<{sample:OaSimulationSample;name:string}[]>([]);
  const [timed,setTimed]=useState(false),[activateAt,setActivateAt]=useState("");
  const submitted=applicant?{employeeId:applicant.id,appointmentId:applicant.appointmentId,amount,currency,duration,category,isReturnable,projectId,formValues,...(hiring?{reporting:{managerEmployeeId:reporting.managerEmployeeId,managerAssignmentId:reporting.managerAssignmentId,functionalManagerEmployeeIds:reporting.functionalManagerEmployeeIds,functionalManagerAssignmentIds:reporting.functionalManagerAssignmentIds}}:{})}:null;
  return <><OfficeDialog title={`${onPublish?"仿真并发布":"模拟流程"} · ${rule.name}`} onClose={onClose} busy={busy||operation.busy} error={error||operation.error}>
    <p>模拟使用正式提交的解析内核，不创建申请或通知。发布时会再次核对所有样例、人员职责和固定子流程版本。</p>
    {hiring&&<p>借用现有任职的单位与部门，按下面填写的拟入职汇报关系验证路线。</p>}
    <form onSubmit={event=>{event.preventDefault();if(!submitted)return;void operation.run(signal=>client.simulateOaDefinition({body:{definition:rule,...submitted}},{signal}),setResult,false);}}>
      <fieldset disabled={busy||operation.busy} className="office-form-grid">
        <div className="office-field-wide"><strong>模拟申请任职：{applicant?.name??"尚未选择"}</strong><Button type="button" onClick={()=>setChoosing(true)}>选择人员任职</Button></div>
        {rule.form&&<OaFormFields required form={rule.form} values={formValues} client={client} company={user.companyScope} onChange={value=>{setFormValues(value);setResult(null);}}/>}
        {hiring&&<div className="office-field-wide"><ReportingEditor client={client} company={user.companyScope} personnel={false} label="拟入职汇报关系" value={reporting} onChange={next=>{setReporting(current=>({...current,...next}));setResult(null);}}/></div>}
        {!hiring&&<WorkflowProjectPicker client={client} user={user} kind={rule.kind} employeeId={applicant?.id} appointmentId={applicant?.appointmentId} value={projectId} onChange={value=>{setProjectId(value);setResult(null);}}/>}
        {(rule.kind==="expense"||rule.kind==="purchase")&&<><OfficeField label="原币金额"><input required inputMode="decimal" value={amount} onChange={e=>{setAmount(e.target.value);setResult(null);}}/></OfficeField><OfficeField label="币种"><select value={currency} onChange={e=>{setCurrency(e.target.value);setResult(null);}}>{["CNY","USD","EUR","HKD","JPY","GBP"].map(code=><option key={code}>{code}</option>)}</select></OfficeField></>}
        {["leave","overtime","travel"].includes(rule.kind)&&<OfficeField label={rule.kind==="overtime"?"时长（小时）":"时长（天）"}><input required inputMode="decimal" value={duration} onChange={e=>{setDuration(e.target.value);setResult(null);}}/></OfficeField>}
        {(rule.kind==="leave"||rule.kind==="general")&&<OfficeField label="类别代码"><input value={category} onChange={e=>{setCategory(e.target.value);setResult(null);}}/></OfficeField>}
        {rule.kind==="supply"&&<label className="checkbox-field"><input type="checkbox" checked={isReturnable} onChange={e=>{setReturnable(e.target.checked);setResult(null);}}/>模拟借用并归还</label>}
      </fieldset><OfficeSubmit busy={busy||operation.busy||!applicant} label="解析模拟路线"/>
    </form>
    {result&&<><OaApprovalSteps plan={result.approvalPlan} notes={result.notes} user={user}/>{onPublish&&<Button type="button" variant="primary" disabled={busy||operation.busy||samples.length>=20} onClick={()=>{if(submitted&&!samples.some(s=>JSON.stringify(s.sample)===JSON.stringify(submitted)))setSamples(current=>[...current,{sample:submitted,name:applicant!.name}]);}}>保留此发布样例</Button>}</>}
    {onPublish&&<form onSubmit={e=>{e.preventDefault();if(!samples.length)return;try{const at=timed?businessDateTimeLocalInputToIso(activateAt,user.businessTimeZone):undefined;if(timed&&!at)return;void onPublish(samples.map(s=>s.sample),at??undefined);}catch(cause){const input=e.currentTarget.querySelector<HTMLInputElement>('input[type="datetime-local"]');input?.setCustomValidity(readApiError(cause));input?.reportValidity();}}}>
      <fieldset disabled={busy||operation.busy}><legend>发布样例（{samples.length}／20）</legend><p>至少保留一个已通过的样例；建议覆盖默认分支、金额边界与不同申请任职。</p><ul>{samples.map((entry,i)=><li key={i}>{entry.name} · {entry.sample.currency} {entry.sample.amount} · 时长 {entry.sample.duration}<Button type="button" onClick={()=>setSamples(current=>current.filter((_,j)=>i!==j))}>移除样例</Button></li>)}</ul>
        <OfficeField label="启用方式"><select value={timed?"scheduled":"now"} onChange={e=>setTimed(e.target.value==="scheduled")}><option value="now">立即启用</option><option value="scheduled">定时启用</option></select></OfficeField>
        {timed&&<OfficeField label={`启用时间（${user.businessTimeZone}）`}><input type="datetime-local" required value={activateAt} onChange={e=>setActivateAt(e.target.value)} onInput={e=>e.currentTarget.setCustomValidity("")}/></OfficeField>}
      </fieldset><OfficeSubmit busy={busy||operation.busy} disabled={!samples.length} label={timed?"保存并定时启用":"保存并发布此流程"}/>
    </form>}
  </OfficeDialog>{choosing&&<OrganizationManagerPicker client={client} companyCode={user.companyScope} onClose={()=>setChoosing(false)} onSelect={person=>{setApplicant({id:person.id,appointmentId:person.appointmentId,name:`${person.fullName} · ${person.departmentName}`});setResult(null);setChoosing(false);}}/>}</>;
}
