export function requestPersonLabel(name: string, person: { employeeId?: number | null; employeeNumber?: string | null }) {
  return `${name} · ${person.employeeNumber ? `工号 ${person.employeeNumber}` : person.employeeId ? `人员 #${person.employeeId}` : "未记录人员编号"}`;
}
