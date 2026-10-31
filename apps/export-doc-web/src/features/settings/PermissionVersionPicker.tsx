import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
export function PermissionVersionPicker({ client, schemeId, value, disabled, onChange }: { client: ExportDocManagerApiClient; schemeId?: number | null; value?: number | null; disabled: boolean; onChange: (version: number | null) => void }) {
  const [page, setPage] = useState(1);
  const query = useQuery({ queryKey: ["permission-versions", schemeId, page], enabled: Boolean(schemeId), queryFn: ({ signal }) => client.getPermissionSchemeVersions({ schemeId: String(schemeId), pageNumber: page, pageSize: 20 }, { signal }) });
  const versions = query.data?.items ?? [];
  return <label className="office-field"><span>权限发布版本</span><select required value={value ?? ""} disabled={disabled || !query.data} onChange={event => onChange(Number(event.target.value) || null)}><option value="">选择已发布版本</option>{value && !versions.some(row => row.version === value) && <option value={value}>已绑定版本 {value}</option>}{versions.map(row => <option key={row.version} value={row.version}>版本 {row.version} · {row.publishedAt}</option>)}</select>{query.isError && <span role="alert">{readApiError(query.error)}</span>}{(query.data?.totalCount ?? 0) > 20 && <span><button type="button" disabled={page === 1 || query.isFetching} onClick={() => setPage(page - 1)}>较新版本</button><button type="button" disabled={page * 20 >= (query.data?.totalCount ?? 0) || query.isFetching} onClick={() => setPage(page + 1)}>较早版本</button></span>}</label>;
}
