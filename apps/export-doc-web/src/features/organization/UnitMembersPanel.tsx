import { useState } from "react";
import { Link } from "react-router-dom";
import { oaAccess } from "../oa/oaModel.ts";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { PersonnelFoundationPanel } from "./PersonnelFoundationPanel.tsx";

export function UnitMembersPanel({ client, user, company }: { client: ExportDocManagerApiClient; user: ApiUserDto; company: string }) {
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [available, setAvailable] = useState(false);
  const query = useQuery({ queryKey: ["unit-members", company, search, page, available], queryFn: ({ signal }) => client.getUnitMembers({ companyCode: company, keyword: search, pageNumber: page, pageSize: 30, includeAvailable: available }, { signal }) });
  return <section aria-label="单位任职管理"><h3>单位任职</h3><p className="office-muted">维护本单位的部门、职位与汇报关系。普通任职持续有效，直到明确调岗、撤任或离职；临时兼任可选结束日期。</p>
    <p>正式调岗与兼职调整由获授权人员提交人员变动申请，批准后生效。</p>
    <form className="office-search" onSubmit={event => { event.preventDefault(); setPage(1); setSearch(keyword); }}><input aria-label="搜索单位任职人员" maxLength={100} value={keyword} onChange={e => setKeyword(e.target.value)} /><button type="submit" className="command-button secondary">搜索</button><button type="button" className="command-button secondary" disabled={query.isFetching} onClick={()=>void query.refetch()}>刷新单位任职</button>
      {user.capabilities.canManageUsers && <label className="checkbox-field"><input type="checkbox" checked={available} onChange={e => { setAvailable(e.target.checked); setPage(1); }} />包含可分配的其他单位人员</label>}
    </form>
    {query.isPending ? <PageState tone="loading" title="正在加载单位任职" /> : query.isError ? <PageState tone="error" title="任职加载失败" description={readApiError(query.error)} action={<button type="button" onClick={() => void query.refetch()}>重试</button>} /> : <>
      {!query.data.items.length && <p>没有符合条件的任职人员。</p>}
      {query.data.items.map(member => <div className="office-resource-card" key={member.personId}><strong>{member.fullName} · {member.employeeNumber}</strong><p>{member.appointments.map(a => `${a.isPrimary ? "主职" : "兼任"}：${query.data.departments.find(d => d.code === a.departmentId)?.name || a.departmentId} · ${a.jobTitle}`).join("；") || "尚无本单位任职"}</p>{member.status !== "Departed" && oaAccess(user, "personnel", "create") && <Link to={`/office/requests/personnel?change=Transfer&employeeId=${member.personId}`}>申请任职变更</Link>}</div>)}
      <div className="office-card-actions"><button type="button" disabled={page === 1 || query.isFetching} onClick={() => setPage(page - 1)}>上一页</button><span>第 {page} 页 · 共 {query.data.totalCount} 人</span><button type="button" disabled={page * 30 >= query.data.totalCount || query.isFetching} onClick={() => setPage(page + 1)}>下一页</button></div>
      <PersonnelFoundationPanel key={query.data.unitId} client={client} user={user} unitId={query.data.unitId} />
    </>}
  </section>;
}
