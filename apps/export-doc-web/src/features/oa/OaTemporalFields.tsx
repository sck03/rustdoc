import type { Dispatch, SetStateAction } from "react";
import type { OaLeave, OaRequestSave } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
export const leaveCategories = { Annual: "年假", Sick: "病假", Personal: "事假", Other: "其他" } as const;
export const generalCategories = { Seal: "用印", Certificate: "证明开具", IT: "IT 支持", Repair: "维修", Other: "其他事项" } as const;

export function OaTemporalFields({ draft, setDraft, timeZone }: { draft: OaRequestSave; setDraft: Dispatch<SetStateAction<OaRequestSave>>; timeZone: string }) {
  return <>
    {draft.leave && <>
      <OfficeField label="请假类型"><select value={draft.leave.category} onChange={(event) => setDraft({ ...draft, leave: { ...draft.leave!, category: event.target.value as OaLeave["category"] } })}>{Object.entries(leaveCategories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></OfficeField>
      <p className="office-muted">以自然日计算半天或整天，不自动排除节假日或扣减假期余额。</p>
      {(["startsOn", "endsOn"] as const).map((field) => <OfficeField key={field} label={field === "startsOn" ? "开始日期" : "结束日期"}><input type="date" required value={draft.leave![field]} onChange={(event) => setDraft({ ...draft, leave: { ...draft.leave!, [field]: event.target.value } })} /></OfficeField>)}
      {(["startPeriod", "endPeriod"] as const).map((field) => <OfficeField key={field} label={field === "startPeriod" ? "开始时段" : "结束时段"}><select value={draft.leave![field]} onChange={(event) => setDraft({ ...draft, leave: { ...draft.leave!, [field]: event.target.value as "AM" | "PM" } })}><option value="AM">上午</option><option value="PM">下午</option></select></OfficeField>)}
    </>}
    {draft.travel && <>
      <OfficeField label="出差地点" wide><input required maxLength={200} value={draft.travel.destination} onChange={(event) => setDraft({ ...draft, travel: { ...draft.travel!, destination: event.target.value } })} /></OfficeField>
      {(["startsOn", "endsOn"] as const).map((field) => <OfficeField key={field} label={field === "startsOn" ? "出发日期" : "返程日期"}><input type="date" required value={draft.travel![field]} onChange={(event) => setDraft({ ...draft, travel: { ...draft.travel!, [field]: event.target.value } })} /></OfficeField>)}
    </>}
    {draft.overtime && <>
      <p className="office-field-wide office-muted">时间按公司业务时区 {timeZone} 填写；每次 15 分钟至 24 小时。</p>
      {(["startsAt", "endsAt"] as const).map((field) => <OfficeField key={field} label={field === "startsAt" ? "开始时间" : "结束时间"}><input type="datetime-local" required value={draft.overtime![field]} onChange={(event) => setDraft({ ...draft, overtime: { ...draft.overtime!, [field]: event.target.value } })} /></OfficeField>)}
      <OfficeField label="加班地点" wide><input required maxLength={200} value={draft.overtime.location} onChange={(event) => setDraft({ ...draft, overtime: { ...draft.overtime!, location: event.target.value } })} /></OfficeField>
    </>}
    {draft.category && <OfficeField label="申请类别"><select value={draft.category} onChange={(event) => setDraft({ ...draft, category: event.target.value as OaRequestSave["category"] })}>{Object.entries(generalCategories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></OfficeField>}
  </>;
}
