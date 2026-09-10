import type { FormEvent } from "react";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { isMeetingBooking, readMeetingBookingUpdate, readSupplyRequestUpdate, type OfficeRequestRow } from "./officeModel.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { useOfficeOperation } from "./useOfficeData.ts";

export function OfficeRequestEditor({ client, user, row, onClose }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; row: OfficeRequestRow; onClose: () => void;
}) {
  const operation = useOfficeOperation();
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    void operation.run<OfficeRequestRow>((signal) => isMeetingBooking(row)
      ? client.updateMeetingBooking({ id: row.id, body: readMeetingBookingUpdate(form, row.versionNumber, user.businessTimeZone) }, { signal })
      : client.updateOfficeSupplyRequest({ id: row.id, body: readSupplyRequestUpdate(form, row.versionNumber) }, { signal }), onClose);
  }
  return <OfficeDialog title={isMeetingBooking(row) ? "修改预约" : "修改领用"} onClose={onClose} {...operation} protectChanges>
    <p>{row.applicantName} · {isMeetingBooking(row) ? row.roomName : row.supplyName}</p>
    <p className="office-muted">{user.capabilities.usesOfficeRegister ? "保存后将按新内容调整时段或预留库存。" : "修改已审批的内容后须重新审批。"}已交接的记录不能修改。</p>
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}>
      {isMeetingBooking(row) ? <>
        <OfficeField label="会议主题" wide><input name="title" required maxLength={200} defaultValue={row.title} autoFocus /></OfficeField>
        <OfficeField label="参会人数"><input name="attendeeCount" type="number" required min={1} max={10000} defaultValue={row.attendeeCount} /></OfficeField>
        <OfficeField label="开始时间"><input name="startsAt" type="datetime-local" required defaultValue={toBusinessDateTimeLocalInput(row.startsAt, user.businessTimeZone)} /></OfficeField>
        <OfficeField label="结束时间"><input name="endsAt" type="datetime-local" required defaultValue={toBusinessDateTimeLocalInput(row.endsAt, user.businessTimeZone)} /></OfficeField>
      </> : <>
        <OfficeField label={`领用数量（${row.unit}）`}><input name="quantity" type="number" min={1} max={1000000} required defaultValue={row.quantity} autoFocus /></OfficeField>
        {row.isReturnable && <OfficeField label="预计归还日期"><input name="returnDueDate" type="date" required min={user.businessDate} defaultValue={row.returnDueDate ?? ""} /></OfficeField>}
        <OfficeField label="用途" wide><textarea name="purpose" rows={3} required maxLength={500} defaultValue={row.purpose} /></OfficeField>
      </>}
    </fieldset><OfficeSubmit busy={operation.busy} label="保存修改" /></form>
  </OfficeDialog>;
}
