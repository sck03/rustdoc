import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Save } from "lucide-react";
import type {
  ApiOrganizationCompanyDto,
  ApiOrganizationDepartmentDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";

type CompanyDraft = { existingCode: string; code: string; name: string; isActive: boolean; expectedVersion: number };
type DepartmentDraft = { existingCode: string; code: string; companyCode: string; name: string; isActive: boolean; expectedVersion: number };

const emptyCompany = (): CompanyDraft => ({ existingCode: "", code: "", name: "", isActive: true, expectedVersion: 0 });
const emptyDepartment = (companyCode = ""): DepartmentDraft => ({
  existingCode: "", code: "", companyCode, name: "", isActive: true, expectedVersion: 0,
});

export function OrganizationDirectoryPanel({ client }: { client: ExportDocManagerApiClient }) {
  const queryClient = useQueryClient();
  const [companyDraft, setCompanyDraft] = useState<CompanyDraft>(emptyCompany);
  const [departmentDraft, setDepartmentDraft] = useState<DepartmentDraft>(emptyDepartment);
  const [message, setMessage] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const directoryQuery = useQuery({
    queryKey: queryKeys.organizationDirectory(),
    queryFn: ({ signal }) => client.getOrganizationDirectory({ signal }),
  });

  async function refreshDirectory() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.organizationDirectory() }),
      queryClient.invalidateQueries({ queryKey: queryKeys.users() }),
    ]);
  }

  const companyMutation = useMutation({
    mutationFn: (draft: CompanyDraft) => draft.existingCode
      ? client.updateOrganizationCompany({
          code: draft.existingCode,
          body: { code: draft.code, name: draft.name, isActive: draft.isActive, expectedVersion: draft.expectedVersion },
        })
      : client.createOrganizationCompany({
          body: { code: draft.code, name: draft.name, isActive: draft.isActive, expectedVersion: 0 },
        }),
    onSuccess: async () => {
      setCompanyDraft(emptyCompany());
      setMessage(null);
      setSuccess("公司目录已保存。");
      await refreshDirectory();
    },
    onError: (error) => { setMessage(readApiError(error)); setSuccess(null); },
  });
  const departmentMutation = useMutation({
    mutationFn: (draft: DepartmentDraft) => draft.existingCode
      ? client.updateOrganizationDepartment({
          code: draft.existingCode,
          body: {
            code: draft.code,
            companyCode: draft.companyCode,
            name: draft.name,
            isActive: draft.isActive,
            expectedVersion: draft.expectedVersion,
          },
        })
      : client.createOrganizationDepartment({
          body: {
            code: draft.code,
            companyCode: draft.companyCode,
            name: draft.name,
            isActive: draft.isActive,
            expectedVersion: 0,
          },
        }),
    onSuccess: async () => {
      setDepartmentDraft(emptyDepartment(companyDraft.code));
      setMessage(null);
      setSuccess("部门目录已保存。");
      await refreshDirectory();
    },
    onError: (error) => { setMessage(readApiError(error)); setSuccess(null); },
  });
  const companies = directoryQuery.data?.companies ?? [];
  const departments = directoryQuery.data?.departments ?? [];
  const busy = directoryQuery.isFetching || companyMutation.isPending || departmentMutation.isPending;

  function editCompany(item: ApiOrganizationCompanyDto) {
    setCompanyDraft({
      existingCode: item.code,
      code: item.code,
      name: item.name,
      isActive: item.isActive,
      expectedVersion: item.versionNumber,
    });
    setMessage(null);
    setSuccess(null);
  }

  function editDepartment(item: ApiOrganizationDepartmentDto) {
    setDepartmentDraft({
      existingCode: item.code,
      code: item.code,
      companyCode: item.companyCode,
      name: item.name,
      isActive: item.isActive,
      expectedVersion: item.versionNumber,
    });
    setMessage(null);
    setSuccess(null);
  }

  return (
    <details className="template-inline-details organization-directory-panel">
      <summary>组织目录（公司 / 部门）</summary>
      <div className="template-inline-details-content">
        <p className="section-description">公司和部门代码是数据范围授权键；代码创建后不可修改，显示名称可调整。</p>
        {directoryQuery.isError || message ? (
          <InlineNotice tone="error" title="组织目录操作失败">{message ?? readApiError(directoryQuery.error)}</InlineNotice>
        ) : null}
        {success ? <InlineNotice tone="success">{success}</InlineNotice> : null}
        <div className="field-grid user-management-form-grid">
          <label>
            <span>公司代码</span>
            <input value={companyDraft.code} disabled={busy || Boolean(companyDraft.existingCode)} onChange={(event) => setCompanyDraft((current) => ({ ...current, code: event.target.value }))} />
          </label>
          <label>
            <span>公司名称</span>
            <input value={companyDraft.name} disabled={busy} onChange={(event) => setCompanyDraft((current) => ({ ...current, name: event.target.value }))} />
          </label>
          <label className="settings-check">
            <input type="checkbox" checked={companyDraft.isActive} disabled={busy} onChange={(event) => setCompanyDraft((current) => ({ ...current, isActive: event.target.checked }))} />
            <span>启用公司</span>
          </label>
          <div className="toolbar-actions">
            <button className="command-button" type="button" disabled={busy || !companyDraft.code.trim() || !companyDraft.name.trim()} onClick={() => companyMutation.mutate(companyDraft)}>
              <Save size={16} aria-hidden="true" /><span>保存公司</span>
            </button>
            <button className="command-button secondary" type="button" disabled={busy} onClick={() => setCompanyDraft(emptyCompany())}>
              <Plus size={16} aria-hidden="true" /><span>新公司</span>
            </button>
          </div>
        </div>
        <div className="compact-option-list" aria-label="公司目录">
          {companies.map((item) => (
            <button className="compact-option-row" type="button" key={item.code} disabled={busy} onClick={() => editCompany(item)}>
              <strong>{item.name}</strong><span>{item.code} · {item.isActive ? "启用" : "停用"}</span>
            </button>
          ))}
        </div>

        <div className="field-grid user-management-form-grid">
          <label>
            <span>所属公司</span>
            <select value={departmentDraft.companyCode} disabled={busy} onChange={(event) => setDepartmentDraft((current) => ({ ...current, companyCode: event.target.value }))}>
              <option value="">请选择公司</option>
              {companies.filter((item) => item.isActive || item.code === departmentDraft.companyCode).map((item) => (
                <option key={item.code} value={item.code}>{item.name}（{item.code}）</option>
              ))}
            </select>
          </label>
          <label>
            <span>部门代码</span>
            <input value={departmentDraft.code} disabled={busy || Boolean(departmentDraft.existingCode)} onChange={(event) => setDepartmentDraft((current) => ({ ...current, code: event.target.value }))} />
          </label>
          <label>
            <span>部门名称</span>
            <input value={departmentDraft.name} disabled={busy} onChange={(event) => setDepartmentDraft((current) => ({ ...current, name: event.target.value }))} />
          </label>
          <label className="settings-check">
            <input type="checkbox" checked={departmentDraft.isActive} disabled={busy} onChange={(event) => setDepartmentDraft((current) => ({ ...current, isActive: event.target.checked }))} />
            <span>启用部门</span>
          </label>
          <div className="toolbar-actions">
            <button className="command-button" type="button" disabled={busy || !departmentDraft.companyCode || !departmentDraft.code.trim() || !departmentDraft.name.trim()} onClick={() => departmentMutation.mutate(departmentDraft)}>
              <Save size={16} aria-hidden="true" /><span>保存部门</span>
            </button>
            <button className="command-button secondary" type="button" disabled={busy} onClick={() => setDepartmentDraft(emptyDepartment(companies.find((item) => item.isActive)?.code ?? ""))}>
              <Plus size={16} aria-hidden="true" /><span>新部门</span>
            </button>
          </div>
        </div>
        <div className="compact-option-list" aria-label="部门目录">
          {departments.map((item) => (
            <button className="compact-option-row" type="button" key={item.code} disabled={busy} onClick={() => editDepartment(item)}>
              <strong>{item.name}</strong><span>{item.code} · {item.companyCode} · {item.isActive ? "启用" : "停用"}</span>
            </button>
          ))}
        </div>
      </div>
    </details>
  );
}
