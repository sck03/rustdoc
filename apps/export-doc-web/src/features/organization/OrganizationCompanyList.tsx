import { useMemo, useState } from "react";
import { Building2 } from "lucide-react";
import type { ApiOrganizationCompanyDto, ApiOrganizationDepartmentDto } from "../../api/index.ts";

export function OrganizationCompanyList({ companies, departments, selectedCode, onSelect }: {
  companies: ApiOrganizationCompanyDto[];
  departments: ApiOrganizationDepartmentDto[];
  selectedCode: string;
  onSelect: (code: string) => void;
}) {
  const [keyword, setKeyword] = useState("");
  const counts = useMemo(() => {
    const result = new Map<string, number>();
    for (const department of departments) result.set(department.companyCode, (result.get(department.companyCode) ?? 0) + 1);
    return result;
  }, [departments]);
  const search = keyword.trim().normalize("NFC").toLocaleLowerCase();
  const filtered = companies.filter((company) => (company.name + " " + company.code).normalize("NFC").toLocaleLowerCase().includes(search));
  return <>
    <aside className="organization-company-directory" aria-label="公司目录">
      <div className="organization-company-directory-title"><strong>公司</strong><span>{companies.length}</span></div>
      <input type="search" aria-label="查找公司" placeholder="搜索公司名称或代码" value={keyword} maxLength={100} onChange={(event) => setKeyword(event.target.value)} />
      <ul className="organization-company-list">
        {filtered.map((company) => <li key={company.code}><button type="button" aria-pressed={company.code === selectedCode} onClick={() => onSelect(company.code)}>
          <Building2 size={16} aria-hidden="true" /><span><strong>{company.name}</strong><small>{company.code}{company.isActive ? "" : " · 已停用"}</small></span>
          <small title="部门数">{counts.get(company.code) ?? 0}</small>
        </button></li>)}
      </ul>
      {!filtered.length && <p className="office-muted">没有匹配的公司</p>}
    </aside>
    <label className="organization-company-switch">切换公司<select value={selectedCode} onChange={(event) => onSelect(event.target.value)}>
      {companies.map((company) => <option key={company.code} value={company.code}>{company.name}{company.isActive ? "" : "（停用）"}</option>)}
    </select></label>
  </>;
}
