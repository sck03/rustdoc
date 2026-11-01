import type { OaBusinessCalendar } from "../../api/index.ts";
import { useState } from "react";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { OfficeField } from "../office/OfficeUi.tsx";
const weekdays=["周一","周二","周三","周四","周五","周六","周日"];
function CalendarDates({label,dates,onChange}:{label:string;dates:string[];onChange:(dates:string[])=>void}) {
  const [text,setText] = useState(() => dates.join("\n"));
  useUnsavedChangesGuard({isDirty:text !== dates.join("\n"),message:"工作日历有未保存的日期输入。"});
  return <OfficeField label={label}><textarea rows={5} value={text} onChange={event=>{
    const value=event.target.value; setText(value); onChange(value.split(/[\s,;]+/).filter(Boolean));
  }}/></OfficeField>;
}
export function BusinessCalendarEditor({value,onChange,disabled=false,resetToken=0}:{value?:OaBusinessCalendar;onChange:(value:OaBusinessCalendar)=>void;disabled?:boolean;resetToken?:number}) {
  if(!value)return <p>正在读取公司工作日历。</p>;
  const patch=(change:Partial<OaBusinessCalendar>)=>onChange({...value,...change});
  return <details className="office-resource-card"><summary>公司工作日历 · 版本 {value.version}</summary><fieldset disabled={disabled} className="office-form-grid">
    <OfficeField label="业务时区"><input required maxLength={100} value={value.timeZone} onChange={e=>patch({timeZone:e.target.value})}/></OfficeField>
    <div className="office-field-wide">{weekdays.map((label,index)=><label className="checkbox-field" key={label}><input type="checkbox" checked={value.workingWeekdays.includes(index+1)} onChange={e=>patch({workingWeekdays:e.target.checked?[...value.workingWeekdays,index+1].sort():value.workingWeekdays.filter(d=>d!==index+1)})}/>{label}</label>)}</div>
    {value.intervals.map((interval,index)=><div className="office-card-actions office-field-wide" key={index}><label>开始 <input type="time" required value={interval.startsAt} onChange={e=>patch({intervals:value.intervals.map((v,i)=>i===index?{...v,startsAt:e.target.value}:v)})}/></label><label>结束 <input type="time" required value={interval.endsAt} onChange={e=>patch({intervals:value.intervals.map((v,i)=>i===index?{...v,endsAt:e.target.value}:v)})}/></label><button type="button" disabled={value.intervals.length===1} onClick={()=>patch({intervals:value.intervals.filter((_,i)=>i!==index)})}>移除时段</button></div>)}
    <button type="button" disabled={value.intervals.length>=8} onClick={()=>patch({intervals:[...value.intervals,{startsAt:"18:00",endsAt:"19:00"}]})}>添加工作时段</button>
    <CalendarDates key={`holidays:${resetToken}`} label="节假日（每行 YYYY-MM-DD）" dates={value.holidays} onChange={holidays=>patch({holidays})}/>
    <CalendarDates key={`working-days:${resetToken}`} label="调休工作日（每行 YYYY-MM-DD）" dates={value.additionalWorkingDays} onChange={additionalWorkingDays=>patch({additionalWorkingDays})}/>
  </fieldset></details>;
}
