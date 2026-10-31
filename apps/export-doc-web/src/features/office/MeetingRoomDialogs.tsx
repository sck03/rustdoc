import { useState, type FormEvent } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, MeetingBookingRecord, MeetingRoomRecord, PersonnelDirectoryRecord } from "../../api/index.ts";
import { businessDateTimeLocalInputToIso, formatBusinessDateTime } from "../../ui/businessTime.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { officeAccess, officeBookingDefaults, readMeetingRoomForm, shiftOfficeBookingEnd } from "./officeModel.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { useMeetingAvailability, useOfficeResourceOperation } from "./useOfficeData.ts";
import { OfficeEmployeePicker } from "./OfficeEmployeePicker.tsx";
import { OfficeApplicantIdentity, canIdentifyOfficeApplicant } from "./OfficeApplicantIdentity.tsx";
import { HandlingServicePicker } from "../oa/HandlingServicePicker.tsx";
import { AttachmentDraftFields } from "../../ui/AttachmentDraftFields.tsx";
import { useAttachmentDraft } from "../../ui/useAttachmentDraft.ts";
import { attachmentUploadForm } from "../../ui/documentAttachments.ts";

export function MeetingRoomEditor({ client, user, room, onClose, onSaved }: { client: ExportDocManagerApiClient; user: ApiUserDto; room?: MeetingRoomRecord; onClose: () => void; onSaved: (saved: MeetingRoomRecord) => void }) {
  const operation = useOfficeResourceOperation("rooms");
  const [handlingKey, setHandlingKey] = useState(room?.handlingKey ?? "");
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const body = { ...readMeetingRoomForm(new FormData(event.currentTarget), room?.versionNumber ?? 0), handlingKey };
    void operation.run((signal) => room ? client.updateMeetingRoom({ id: room.id, body }, { signal }) : client.createMeetingRoom({ body }, { signal }), onSaved);
  }
  return <OfficeDialog title={room ? "编辑会议室" : "添加会议室"} onClose={onClose} {...operation} protectChanges>
    <form onSubmit={submit}><fieldset disabled={operation.busy} className="office-form-grid">
      <OfficeField label="会议室名称" wide><input name="name" required maxLength={120} defaultValue={room?.name} /></OfficeField>
      <HandlingServicePicker client={client} user={user} category="Room" value={handlingKey} onChange={setHandlingKey} />
      <OfficeField label="位置" wide><input name="location" maxLength={200} defaultValue={room?.location} placeholder="例如：办公楼三层东侧" /></OfficeField>
      <OfficeField label="容纳人数"><input name="capacity" type="number" required min={1} max={10000} defaultValue={room?.capacity ?? 10} /></OfficeField>
      <OfficeField label="单次最长预约（小时）"><input name="maximumBookingHours" type="number" required min={1} max={24} defaultValue={room?.maximumBookingHours ?? 8} /></OfficeField>
      <OfficeField label="可提前预约（天）"><input name="advanceBookingDays" type="number" required min={1} max={365} defaultValue={room?.advanceBookingDays ?? 90} /></OfficeField>
      <OfficeField label="设备及使用说明" wide><textarea name="equipment" rows={3} maxLength={500} defaultValue={room?.equipment} placeholder="投影、白板、视频会议设备等" /></OfficeField>
      <label className="checkbox-field"><input name="requiresKey" type="checkbox" defaultChecked={room?.requiresKey ?? true} />需要钥匙领还</label>
      <label className="checkbox-field"><input name="isActive" type="checkbox" defaultChecked={room?.isActive ?? true} />启用会议室</label>
    </fieldset><OfficeSubmit busy={operation.busy} /></form>
  </OfficeDialog>;
}

export function MeetingBookingDialog({ client, room, user, onClose }: {
  client: ExportDocManagerApiClient; room: MeetingRoomRecord; user: ApiUserDto; onClose: () => void;
}) {
  const [times, setTimes] = useState(() => officeBookingDefaults(user.businessTimeZone));
  const [requestKey] = useState(createRequestKey);
  const attachments = useAttachmentDraft<MeetingBookingRecord>();
  const [employee, setEmployee] = useState<PersonnelDirectoryRecord | null>(null);
  const canSelectEmployee = user.capabilities.canManageUsers;
  const operation = useOfficeResourceOperation("rooms");
  const { range, query: availability } = useMeetingAvailability(client, user, room.id, times.start.slice(0, 10));
  const access = officeAccess(user, "rooms");
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canIdentifyOfficeApplicant(user)) return;
    const form = new FormData(event.currentTarget);
    void operation.run(async (signal) => {
      const startsAt = businessDateTimeLocalInputToIso(times.start, user.businessTimeZone);
      const endsAt = businessDateTimeLocalInputToIso(times.end, user.businessTimeZone);
      if (!startsAt || !endsAt) throw new Error("请选择预约开始和结束时间。");
      const body = { requestKey, meetingRoomId: room.id, title: String(form.get("title") ?? ""), attendeeCount: Number(form.get("attendeeCount")), startsAt, endsAt, employeeId: employee?.id ?? null, submitImmediately:false };
      const saved = await attachments.save(JSON.stringify(body),signal,(current,signal)=>current ? client.updateDraftOfMeetingBooking({id:current.id,body:{expectedVersion:current.versionNumber,title:body.title,attendeeCount:body.attendeeCount,startsAt,endsAt}},{signal}):client.createMeetingBooking({body},{signal}),
        (current,entry,signal)=>{const body=attachmentUploadForm(entry.file,current.versionNumber);body.set("purpose","Supporting");return client.uploadAttachmentToMeetingBooking({id:current.id,body},{signal});});
      return client.submitMeetingBooking({id:saved.id,body:officeCommand(client,`rooms:${saved.id}:submit`,{commandKey:undefined,expectedVersion:saved.versionNumber,note:""})},{signal});
    }, onClose);
  }
  return <OfficeDialog title={`${room.name} · 日程与预约`} onClose={onClose} {...operation} protectChanges>
    <p className="office-muted">{room.location || "位置未填写"} · 最多 {room.capacity} 人 · 单次最多 {room.maximumBookingHours} 小时</p>
    <p className="office-muted">时间按公司业务时区 {user.businessTimeZone} 显示。</p>
    {room.handlingName && <p>办理分工：{room.handlingName} · 办理人员：{room.handlerNames}</p>}
    {operation.error && attachments.saved && <p role="status">草稿 #{attachments.saved.id} 已保存。剩余附件可重试，也可稍后从申请草稿继续办理。</p>}
    <form onSubmit={submit}><fieldset disabled={operation.busy} className="office-form-grid">
      <OfficeField label="开始时间"><input type="datetime-local" value={times.start} required onChange={(event) => {
        const start = event.target.value;
        setTimes({ start, end: shiftOfficeBookingEnd(start, user.businessTimeZone) || times.end });
      }} /></OfficeField>
      <OfficeField label="结束时间"><input type="datetime-local" value={times.end} required onChange={(event) => setTimes({ ...times, end: event.target.value })} /></OfficeField>
      <div className="office-field-wide office-availability" aria-label="所选日期的已占用时段">
        <h3>{times.start.slice(0, 10)} 已占用时段</h3>
        {!range ? <PageState title="请选择完整日期" /> : availability.isPending ? <PageState tone="loading" title="正在查询日程" />
          : availability.isError ? <InlineNotice tone="error">{readApiError(availability.error)}</InlineNotice>
          : !availability.data?.length ? <p>当天暂无预约。</p> : <ul className="office-slot-list">{availability.data.map((slot, index) =>
            <li key={`${slot.startsAt}-${index}`}><span>{formatBusinessDateTime(slot.startsAt, user.businessTimeZone)} — {formatBusinessDateTime(slot.endsAt, user.businessTimeZone)}</span>
              <strong>{slot.status === "Pending" ? "待审批" : slot.status === "InUse" ? "使用中" : "已预约"}</strong></li>)}</ul>}
      </div>
      {access.allows("create") && room.isActive && <>
        {canSelectEmployee && <OfficeEmployeePicker client={client} user={user} value={employee} onChange={setEmployee} disabled={operation.busy} />}
        <OfficeApplicantIdentity user={user} />
        <OfficeField label="会议主题" wide><input name="title" required maxLength={200} placeholder="例如：项目周会" /></OfficeField>
        <OfficeField label="参会人数"><input name="attendeeCount" type="number" required min={1} max={room.capacity} defaultValue={1} /></OfficeField>
        <AttachmentDraftFields draft={attachments} busy={operation.busy} existingFiles={attachments.saved?.attachments}/>
        <p className="office-field-wide office-muted">{`提交后等待审批。${room.requiresKey ? "审批通过后，于开始前 30 分钟内联系办理人领钥匙，结束后归还。" : "审批通过后，请由办理人登记使用和结束。"}`}</p>
      </>}
    </fieldset>{access.allows("create") && room.isActive && <OfficeSubmit label="提交预约" busy={operation.busy}
      disabled={!range || availability.isPending || availability.isError || !canIdentifyOfficeApplicant(user) || canSelectEmployee && !employee} />}</form>
  </OfficeDialog>;
}
import { officeCommand } from "./officeCommands.ts";
