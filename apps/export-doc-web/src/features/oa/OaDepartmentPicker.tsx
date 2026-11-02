import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";

export function OaDepartmentPicker({ client, user, employeeId, value, onChange }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; employeeId?: number;
  value?: string; onChange: (appointment: { id: string; departmentId: string }) => void;
}) {
  const person = useQuery({
    queryKey: ["office", "people", "request-departments", user.id, user.companyScope, employeeId],
    enabled: Boolean(employeeId),
    queryFn: ({ signal }) => client.getPersonnel({ id: employeeId! }, { signal }),
  });
  const employee = employeeId ? person.data?.employee : user.employee;
  const organization = employeeId ? person.data?.organization : user.employee?.organization;
  if (person.isError) return <InlineNotice tone="error">{readApiError(person.error)}</InlineNotice>;
  if (!employee || !organization?.primaryAssignmentId) return null;
  const primary = { id: organization.primaryAssignmentId, departmentId: employee.departmentId ?? "", jobTitle: employee.jobTitle, companyCode: employeeId ? person.data?.companyScope : user.primaryCompanyScope ?? user.companyScope };
  const currentId = user.employee?.appointmentId;
  const appointments = [primary, ...organization.additionalAssignments].filter(appointment =>
    appointment.companyCode === user.companyScope &&
    (!user.contextId || user.capabilities.canManageUsers || appointment.id === currentId) &&
    (!("startsOn" in appointment) || !appointment.startsOn || appointment.startsOn <= user.businessDate) &&
    (!("endsOn" in appointment) || !appointment.endsOn || appointment.endsOn >= user.businessDate));
  const selected = value || currentId || primary.id;
  return <OfficeField label="申请任职"><select value={selected} onChange={event => {
    const appointment = appointments.find(item => item.id === event.target.value);
    if (appointment?.id) onChange({ id: appointment.id, departmentId: appointment.departmentId });
  }}>{appointments.map(appointment => <option key={appointment.id} value={appointment.id}>
    {appointment.departmentId} · {appointment.jobTitle}{appointment.id === primary.id ? "（主职）" : "（兼职）"}
  </option>)}</select></OfficeField>;
}
