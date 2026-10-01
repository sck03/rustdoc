export type PermissionSubject = { id: number; companyScope: string; departmentId: string };
export type PermissionRecord = { ownerUserId?: number | null; companyScope: string; departmentId: string };

export function isRecordInPermissionScope(scope: string, subject: PermissionSubject | undefined, record: PermissionRecord | null, administrator = false) {
  if (!record || !subject || !scope) return false;
  if (administrator || scope === "all") return true;
  if (record.companyScope !== subject.companyScope) return false;
  if (scope === "company") return true;
  if (scope === "department") return record.departmentId === subject.departmentId;
  return scope === "own" && record.ownerUserId === subject.id;
}
