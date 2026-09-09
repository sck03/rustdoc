import { useState } from "react";
import { Link } from "react-router-dom";
import type { ApiOrganizationCompanyDto, ApiOrganizationDepartmentDto, ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { filterDepartmentTree } from "./organizationModel.ts";
import { useOrganizationDirectory } from "./useOrganizationDirectory.ts";
import { OrganizationCompanyForm, OrganizationDepartmentForm } from "./OrganizationForms.tsx";
import { OrganizationTree } from "./OrganizationTree.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/organization.css";

export function OrganizationDirectoryPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  return user.capabilities.canManageUsers ? <DirectoryWorkspace client={client} user={user} /> : null;
}

function DirectoryWorkspace({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const query = useOrganizationDirectory(client);
  const [companyCode, setCompanyCode] = useState(user.companyScope);
  const [search, setSearch] = useState("");
  const [companyEditor, setCompanyEditor] = useState<{ record?: ApiOrganizationCompanyDto } | null>(null);
  const [departmentEditor, setDepartmentEditor] = useState<{ record?: ApiOrganizationDepartmentDto; parentCode?: string } | null>(null);
  const companies = query.data?.companies ?? [];
  const company = companies.find((item) => item.code === companyCode) ?? companies.find((item) => item.isActive) ?? companies[0];
  const departments = (query.data?.departments ?? []).filter((item) => item.companyCode === company?.code);
  const visible = filterDepartmentTree(departments, search);
  return <section className="office-workspace organization-workspace" aria-label="组织架构管理">
    <div className="organization-heading"><div><h2>组织架构</h2><p className="office-muted">维护公司、部门层级和负责人，供人员档案及账号归属统一使用。</p></div>
      <div className="office-card-actions"><Link className="command-button secondary" to="/office/people">人员档案</Link>
        <button type="button" className="command-button" onClick={() => setCompanyEditor({})}>新增公司</button></div></div>
    {query.isPending ? <PageState tone="loading" title="正在加载组织架构" /> : query.isError ? <PageState tone="error" title="组织架构加载失败" description={readApiError(query.error)}
      action={<button type="button" onClick={() => void query.refetch()}>重新加载</button>} /> : !company ? <PageState title="尚未设置公司" description="先新增公司，再在公司下建立部门。" /> : <>
      <div className="office-toolbar"><label className="office-filter">公司<select value={company.code} onChange={(event) => { setCompanyCode(event.target.value); setSearch(""); }}>
        {companies.map((item) => <option key={item.code} value={item.code}>{item.name}{item.isActive ? "" : "（停用）"}</option>)}</select></label>
        <button type="button" className="command-button secondary" onClick={() => setCompanyEditor({ record: company })}>维护公司</button>
        <button type="button" className="command-button" disabled={!company.isActive} onClick={() => setDepartmentEditor({})}>新增部门</button>
        <button type="button" className="command-button secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>刷新</button>
      </div>
      <p className="office-muted">{company.name} · {departments.length} 个部门{company.code === user.companyScope ? " · 当前账号所属公司" : ""}</p>
      <label className="office-field"><span>查找部门或负责人</span><input type="search" maxLength={100} value={search} onChange={(event) => setSearch(event.target.value)} placeholder="部门名称、代码、负责人" /></label>
      {visible.length ? <OrganizationTree key={`${company.code}/${search}`} departments={visible} canAdd={company.isActive}
        onEdit={(record) => setDepartmentEditor({ record })} onAdd={(parentCode) => setDepartmentEditor({ parentCode })} />
        : <PageState title={search ? "没有符合条件的部门" : "该公司尚未设置部门"} description={search ? "尝试其他部门名称或负责人。" : "点击“新增部门”，可按实际公司架构逐级建立部门。"} />}
    </>}
    {companyEditor && <OrganizationCompanyForm client={client} record={companyEditor.record} onClose={() => setCompanyEditor(null)}
      onSaved={(saved) => { setCompanyCode(saved.code); setCompanyEditor(null); }} />}
    {departmentEditor && company && <OrganizationDepartmentForm client={client} company={company} departments={departments} {...departmentEditor}
      onClose={() => setDepartmentEditor(null)} onSaved={() => setDepartmentEditor(null)} />}
  </section>;
}
