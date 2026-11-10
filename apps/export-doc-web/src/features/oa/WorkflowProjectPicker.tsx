import { Button } from "../../ui/Button.tsx";
import { useState } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, GetOaRequestProjectsRequest } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";

export function WorkflowProjectPicker({ client, user, kind, employeeId, appointmentId, departmentId, value, onChange }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; kind: GetOaRequestProjectsRequest["kind"];
  employeeId?: number; appointmentId?: string; departmentId?: string; value?: string | null;
  onChange: (id: string | null) => void;
}) {
  const [opened, setOpened] = useState(Boolean(value));
  const applicant = employeeId ?? user.employee?.id;
  const query = useInfiniteQuery({
    queryKey: ["office", "request-projects", user.id, user.companyScope, user.contextId, kind, applicant, appointmentId, departmentId],
    initialPageParam: 1,
    enabled: opened && Boolean(applicant),
    queryFn: ({ signal, pageParam }) => client.getOaRequestProjects({ kind, employeeId: applicant, appointmentId, departmentId, pageNumber: pageParam, pageSize: 100 }, { signal }),
    getNextPageParam: (last, pages) => pages.reduce((count, page) => count + page.items.length, 0) < last.totalCount ? pages.length + 1 : undefined,
    refetchOnWindowFocus: false,
  });
  const projects = query.data?.pages.flatMap(page => page.items) ?? [];
  return <details className="office-field-wide" open={opened} onToggle={event => setOpened(event.currentTarget.open)}>
    <summary>项目审批范围（可选）</summary>
    {!applicant ? <p>请先选择申请人员及任职。</p> : query.isError ? <InlineNotice tone="error">{readApiError(query.error)}</InlineNotice> : <>
      <OfficeField label="申请项目"><select value={value ?? ""} disabled={query.isPending} onChange={event => onChange(event.target.value || null)}>
        <option value="">不涉及项目</option>
        {value && !projects.some(project => project.id === value) && <option value={value}>原项目待核对</option>}
        {projects.map(project => <option key={project.id} value={project.id}>{project.name} · {project.code}</option>)}
      </select></OfficeField>
      {query.hasNextPage && <Button type="button" disabled={query.isFetchingNextPage} onClick={() => void query.fetchNextPage()}>加载更多项目</Button>}
      {query.isSuccess && !projects.length && <p>当前任职没有有效项目成员关系。</p>}
    </>}
  </details>;
}
