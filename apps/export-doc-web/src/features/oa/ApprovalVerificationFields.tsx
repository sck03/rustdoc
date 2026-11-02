import type { OaApprovalNode } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
export function ApprovalVerificationFields({node,values,points,onValues,onPoints,disabled=false,requireCompletion=true}:{node?:Pick<OaApprovalNode,"materialPolicy"|"reviewPoints">;values:Record<string,string|boolean>;points:number[];onValues:(values:Record<string,string|boolean>)=>void;onPoints:(values:number[])=>void;disabled?:boolean;requireCompletion?:boolean}) {
  if(!node)return null;
  const set=(key:string,value:string|boolean)=>{const next={...values};if(value==="")delete next[key];else next[key]=value;onValues(next);};
  return <fieldset disabled={disabled}><legend>本节点审查与核验</legend>
    {node.materialPolicy?.requireReviewPoints&&node.reviewPoints?.map((point,index)=><label className="checkbox-field" key={index}><input type="checkbox" required={requireCompletion} checked={points.includes(index)} onChange={e=>onPoints(e.target.checked?[...points,index]:points.filter(p=>p!==index))}/>{point}</label>)}
    {node.materialPolicy?.verificationFields?.map(field=><OfficeField key={field.key} label={field.label}>{field.kind==="Boolean"?<select required={requireCompletion&&field.required} value={values[field.key]===true?"true":values[field.key]===false?"false":""} onChange={e=>set(field.key,e.target.value===""?"":e.target.value==="true")}><option value="">选择核验结果</option><option value="true">是</option><option value="false">否</option></select>:<input required={requireCompletion&&field.required} type={field.kind==="Date"?"date":"text"} inputMode={field.kind==="Decimal"?"decimal":undefined} maxLength={1000} value={String(values[field.key]??"")} onChange={e=>set(field.key,e.target.value)}/>}</OfficeField>)}
  </fieldset>;
}
