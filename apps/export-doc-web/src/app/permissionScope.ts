export type PermissionSubject = { id: number; companyScope: string; departmentId: string; effectiveDepartmentIds?: string[]; employee?: { organization?: { additionalAssignments: { departmentId: string }[] } } | null };
export type PermissionRecord = { ownerUserId?: number | null; companyScope: string; departmentId: string };

export function hasDepartment(subject: Pick<PermissionSubject, "departmentId" | "employee" | "effectiveDepartmentIds">, department: string) {
  return subject.effectiveDepartmentIds ? subject.effectiveDepartmentIds.includes(department)
    : subject.departmentId === department || subject.employee?.organization?.additionalAssignments.some(assignment => assignment.departmentId === department) === true;
}

export function isRecordInPermissionScope(scope: string, subject: PermissionSubject | undefined, record: PermissionRecord | null, administrator = false) {
  if (!record || !subject || !scope) return false;
  if (administrator || scope === "all") return true;
  if (record.companyScope !== subject.companyScope) return false;
  if (scope === "company") return true;
  if (scope === "department") return hasDepartment(subject, record.departmentId);
  return scope === "own" && record.ownerUserId === subject.id;
}
