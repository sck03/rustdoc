import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";

export function OaDepartmentPicker({ client, user, employeeId, value, onChange }: { client: ExportDocManagerApiClient; user: ApiUserDto; employeeId?: number; value?: string; onChange: (value: string) => void }) {
  const person = useQuery({ queryKey: ["office", "people", "request-departments", user.id, employeeId], enabled: Boolean(employeeId) && Boolean(employeeId), queryFn: ({ signal }) => client.getPersonnel({ id: employeeId! }, { signal }) });
  const primary = Boolean(employeeId) ? person.data?.employee.departmentId : user.employee?.departmentId;
  const organization = Boolean(employeeId) ? person.data?.organization : user.employee?.organization;
  if (person.isError) return <InlineNotice tone="error">{readApiError(person.error)}</InlineNotice>;
  if (!primary) return null;
  return <OfficeField label="申请任职部门"><select value={value || primary} onChange={event => onChange(event.target.value)}><option value={primary}>{primary}（主职）</option>{organization?.additionalAssignments.map(assignment => <option key={assignment.departmentId} value={assignment.departmentId}>{assignment.departmentId} · {assignment.jobTitle}（兼职）</option>)}</select></OfficeField>;
}
