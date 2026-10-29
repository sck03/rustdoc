import { useState } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, PersonnelRecord } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { usePersonnelOperation } from "./usePersonnelData.ts";

export function PersonnelPrimaryAppointmentDialog({ client, user, record, onClose }: { client: ExportDocManagerApiClient; user: ApiUserDto; record: PersonnelRecord; onClose: () => void }) {
  const operation = usePersonnelOperation();
  const [appointmentId, setAppointmentId] = useState("");
  const [note, setNote] = useState("");
  const appointments = record.organization?.additionalAssignments.filter(item => item.id && !item.endsOn && (!item.startsOn || item.startsOn <= user.businessDate)) ?? [];
  return <OfficeDialog title="调整主任职" onClose={onClose} {...operation} protectChanges hasChanges={Boolean(appointmentId || note)}>
    <p>新主职从已登记的有效任职中选择。原主职保留为兼任职，原单位业务资料继续保留原归属；账号所属单位同步更新，已有会话须重新登录。</p>
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => client.promotePersonnelAppointment({ id: record.employee.id, body: { appointmentId, expectedVersion: record.versionNumber, note } }, { signal }), onClose); }}>
      <fieldset className="office-form-grid" disabled={operation.busy}>
        <OfficeField label="新主任职" wide><select required value={appointmentId} onChange={event => setAppointmentId(event.target.value)}><option value="">选择已生效且无结束日期的任职</option>{appointments.map(item => <option key={item.id} value={item.id}>{item.companyCode} · {item.departmentName || item.departmentId} · {item.jobTitle}</option>)}</select></OfficeField>
        {!appointments.length && <p>请先登记目标单位任职，或维护已有任职的有效期。</p>}
        <OfficeField label="主职调动说明" wide><textarea required maxLength={500} value={note} onChange={event => setNote(event.target.value)} /></OfficeField>
        <p>保存时会核对未结事项、审批身份及职责引用；存在影响时须先交接。</p>
      </fieldset><OfficeSubmit busy={operation.busy} label="保存主职调动" />
    </form>
  </OfficeDialog>;
}
