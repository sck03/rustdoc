import type { ApiUserDto, ExportDocManagerApiClient, PersonnelDirectoryRecord } from "../../api/index.ts";
import { OaDepartmentPicker } from "../oa/OaDepartmentPicker.tsx";
import { WorkflowProjectPicker } from "../oa/WorkflowProjectPicker.tsx";
import { OfficeApplicantIdentity } from "./OfficeApplicantIdentity.tsx";
import { OfficeEmployeePicker } from "./OfficeEmployeePicker.tsx";

/** A persisted draft retains its applicant and appointment across upload retries. */
export function OfficeRequestIdentity({ client, user, kind, employee, appointment, projectId, locked,
  onEmployee, onAppointment, onProject }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; kind: "room" | "supply";
  employee: PersonnelDirectoryRecord | null; appointment: { id?: string; departmentId?: string };
  projectId: string | null; locked: boolean;
  onEmployee: (employee: PersonnelDirectoryRecord | null) => void;
  onAppointment: (appointment: { id: string; departmentId: string }) => void;
  onProject: (projectId: string | null) => void;
}) {
  return <fieldset className="permission-fieldset office-field-wide office-form-grid" disabled={locked}>
    {user.capabilities.canManageUsers && <OfficeEmployeePicker client={client} user={user} value={employee}
      disabled={locked} onChange={value => { if (!locked) onEmployee(value); }} />}
    <OfficeApplicantIdentity user={user} />
    <OaDepartmentPicker client={client} user={user} employeeId={employee?.id} value={appointment.id}
      onChange={value => { if (!locked) onAppointment(value); }} />
    <WorkflowProjectPicker client={client} user={user} kind={kind} employeeId={employee?.id}
      appointmentId={appointment.id} departmentId={appointment.departmentId} value={projectId}
      onChange={value => { if (!locked) onProject(value); }} />
    {locked && <p className="office-field-wide office-muted">草稿已保存，申请身份已锁定。</p>}
  </fieldset>;
}
