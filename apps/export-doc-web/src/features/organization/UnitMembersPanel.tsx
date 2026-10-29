import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, UnitMember, UnitMemberAppointment, UnitMemberDirectory } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { ReportingEditor } from "../office/PersonnelOrganizationEditor.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";

export function UnitMembersPanel({ client, user, company }: { client: ExportDocManagerApiClient; user: ApiUserDto; company: string }) {
  const cache = useQueryClient();
  const [editing, setEditing] = useState<UnitMember | null>(null);
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [available, setAvailable] = useState(false);
  const query = useQuery({ queryKey: ["unit-members", company, search, page, available], queryFn: ({ signal }) => client.getUnitMembers({ companyCode: company, keyword: search, pageNumber: page, pageSize: 30, includeAvailable: available }, { signal }) });
  return <section aria-label="单位任职管理"><h3>单位任职</h3><p className="office-muted">维护本单位的部门、职位、有效期与汇报关系。主任职所属单位调整和全局离职由人事办理。</p>
    <form className="office-search" onSubmit={event => { event.preventDefault(); setPage(1); setSearch(keyword); }}><input aria-label="搜索单位任职人员" maxLength={100} value={keyword} onChange={e => setKeyword(e.target.value)} /><button type="submit" className="command-button secondary">搜索</button>
      {user.capabilities.canManageUsers && <label className="checkbox-field"><input type="checkbox" checked={available} onChange={e => { setAvailable(e.target.checked); setPage(1); }} />包含可分配的其他单位人员</label>}
    </form>
    {query.isPending ? <PageState tone="loading" title="正在加载单位任职" /> : query.isError ? <PageState tone="error" title="任职加载失败" description={readApiError(query.error)} action={<button type="button" onClick={() => void query.refetch()}>重试</button>} /> : <>
      {!query.data.items.length && <p>没有符合条件的任职人员。</p>}
      {query.data.items.map(member => <div className="office-resource-card" key={member.personId}><strong>{member.fullName} · {member.employeeNumber}</strong><p>{member.appointments.map(a => `${a.isPrimary ? "主职" : "兼任"}：${query.data.departments.find(d => d.code === a.departmentId)?.name || a.departmentId} · ${a.jobTitle}`).join("；") || "尚无本单位任职"}</p><button type="button" className="text-button" disabled={member.status === "Departed"} onClick={() => setEditing(member)}>维护本单位任职</button></div>)}
      <div className="office-card-actions"><button type="button" disabled={page === 1 || query.isFetching} onClick={() => setPage(page - 1)}>上一页</button><span>第 {page} 页 · 共 {query.data.totalCount} 人</span><button type="button" disabled={page * 30 >= query.data.totalCount || query.isFetching} onClick={() => setPage(page + 1)}>下一页</button></div>
    </>}
    {editing && query.data && <MemberEditor client={client} company={company} member={editing} directory={query.data} onClose={() => setEditing(null)} onSaved={async () => { setEditing(null); await Promise.all([cache.invalidateQueries({ queryKey: ["unit-members"] }), cache.invalidateQueries({ queryKey: ["unit-access"] }), cache.invalidateQueries({ queryKey: ["organization"] })]); }} />}
  </section>;
}

function MemberEditor({ client, company, member, directory, onClose, onSaved }: { client: ExportDocManagerApiClient; company: string; member: UnitMember; directory: UnitMemberDirectory; onClose: () => void; onSaved: () => Promise<void> }) {
  const [appointments, setAppointments] = useState(member.appointments);
  const [note, setNote] = useState("");
  const operation = useOfficeOperation();
  const patch = (index: number, update: Partial<UnitMemberAppointment>) => setAppointments(old => old.map((a, i) => i === index ? { ...a, ...update } : a));
  return <OfficeDialog title={`${member.fullName} · 单位任职`} onClose={onClose} {...operation} protectChanges hasChanges={note !== "" || JSON.stringify(appointments) !== JSON.stringify(member.appointments)}>
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => client.saveUnitMember({ body: { personId: member.personId, companyCode: company, expectedVersion: member.versionNumber, appointments, note } }, { signal }), onSaved); }} onInvalidCapture={event => { const details = (event.target as HTMLElement).closest("details"); if (details) details.open = true; }}>
      <fieldset disabled={operation.busy}>{appointments.map((a, index) => <section className="office-resource-card" key={index}><h4>{a.isPrimary ? "主任职" : `兼任 ${index + 1}`}</h4><div className="office-form-grid">
        <OfficeField label="任职部门"><select required value={a.departmentId} onChange={e => patch(index, { departmentId: e.target.value })}><option value="">选择部门</option>{directory.departments.filter(d => d.isActive || d.code === a.departmentId).map(d => <option key={d.code} value={d.code}>{d.name}{d.isActive ? "" : "（停用）"}</option>)}</select></OfficeField>
        <OfficeField label="职位"><input required maxLength={120} value={a.jobTitle} onChange={e => patch(index, { jobTitle: e.target.value })} /></OfficeField>
        <OfficeField label="生效日期"><input type="date" disabled={a.isPrimary} value={a.startsOn} max={a.endsOn || undefined} onChange={e => patch(index, { startsOn: e.target.value })} /></OfficeField>
        {!a.isPrimary && <OfficeField label="终止日期（选填）"><input type="date" value={a.endsOn} min={a.startsOn || undefined} onChange={e => patch(index, { endsOn: e.target.value })} /></OfficeField>}
      </div><details><summary>直属与职能汇报</summary><ReportingEditor client={client} company={company} personnel={false} label="任职汇报" value={a} onChange={value => patch(index, value)} /></details>
        {!a.isPrimary && <button type="button" className="text-button" onClick={() => setAppointments(old => old.filter((_, i) => i !== index))}>移除此任职</button>}
      </section>)}
      <button type="button" className="command-button secondary" disabled={appointments.length >= 20} onClick={() => setAppointments(old => [...old, { companyCode: company, departmentId: "", jobTitle: "", startsOn: "", endsOn: "", isPrimary: false, managerEmployeeId: null, functionalManagerEmployeeIds: [] }])}>添加本单位任职</button>
      <OfficeField label="变更说明"><textarea required maxLength={500} value={note} onChange={e => setNote(e.target.value)} /></OfficeField></fieldset><OfficeSubmit busy={operation.busy} label="保存任职" />
    </form>
  </OfficeDialog>;
}
