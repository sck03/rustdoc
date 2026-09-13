import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";

export function ReportTemplateVersionHistory({ client, template, enabled, isBusy, onRestore }: {
  client: ExportDocManagerApiClient; template: ApiUserReportTemplateDto; enabled: boolean; isBusy: boolean; onRestore: (version: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);
  const query = useQuery({
    queryKey: [...queryKeys.userReportTemplateVersions(template.id), pageNumber, template.versionNumber],
    queryFn: ({ signal }) => client.listUserReportTemplateVersions({ id: template.id, pageNumber, pageSize: 20 }, { signal }),
    enabled: enabled && open,
    staleTime: 30_000,
  });
  return <details className="template-inline-details" open={open} onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>版本历史{query.data ? ` (${query.data.totalCount})` : ""}</summary>
    {open ? <div className="template-version-list">
      {query.isPending ? <small role="status">正在读取历史版本…</small> : null}
      {query.isError ? <div role="alert">{readApiError(query.error)}<button className="command-button secondary compact-button" type="button" onClick={() => void query.refetch()}>重新读取</button></div> : null}
      {query.data?.items.length === 0 ? <small>保存后会在这里保留可恢复快照。</small> : null}
      {query.data?.items.map(version => <div className="template-version-row" key={version.id}>
        <div><strong>V{version.versionNumber} · {version.changeType}</strong><small>{version.changedBy || "当前用户"} · {new Date(version.createdAt).toLocaleString()}</small></div>
        {template.canEdit && version.canRestore && version.versionNumber !== template.versionNumber ? <button className="command-button secondary compact-button" type="button" disabled={isBusy} onClick={() => onRestore(version.versionNumber)}>恢复</button> : null}
      </div>)}
      {query.data && query.data.totalPages > 1 ? <div className="template-management-actions">
        <button className="command-button secondary compact-button" type="button" disabled={!query.data.hasPreviousPage || query.isFetching} onClick={() => setPageNumber(value => value - 1)}>上一页</button>
        <small>{pageNumber} / {query.data.totalPages} 页</small>
        <button className="command-button secondary compact-button" type="button" disabled={!query.data.hasNextPage || query.isFetching} onClick={() => setPageNumber(value => value + 1)}>下一页</button>
      </div> : null}
    </div> : null}
  </details>;
}
