import { Button } from "../../ui/Button.tsx";
import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OfficeHandlingService } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeField } from "../office/OfficeUi.tsx";

export function HandlingServicePicker({ client, user, category, value, onChange, autoSelect = false }: { client: ExportDocManagerApiClient; user: ApiUserDto; category: OfficeHandlingService["category"]; value: string; onChange: (key: string, revision?: number) => void; autoSelect?: boolean }) {
  const supply = category === "Supply";
  const room = category === "Room";
  const directory = supply ? "supply" : room ? "room" : "general";
  const [keyword, setKeyword] = useState("");
  const [cursors, setCursors] = useState([""]);
  useEffect(() => { setKeyword(""); setCursors([""]); }, [category]);
  const query = useQuery({ queryKey: ["office", "handling-services", user.id, user.companyScope, directory, category, keyword, value, cursors.at(-1)], queryFn: ({ signal }) => {
    const params = { category, keyword, selected: value, state: "active" as const, after: cursors.at(-1), pageSize: 50 };
    return supply ? client.listSupplyHandlingServices(params, { signal }) : room ? client.listRoomHandlingServices(params, { signal }) : client.listGeneralHandlingServices(params, { signal });
  } });
  const options = [...(query.data?.items ?? [])];
  if (query.data?.selected && !options.some(row => row.key === value)) options.unshift(query.data.selected);
  const selected = options.find(row => row.key === value);
  const available = options.filter(row => row.isActive);
  const only = !keyword && cursors.length === 1 && !query.data?.nextCursor && available.length === 1 ? available[0].key : "";
  const revision = only ? available[0].revision : undefined;
  useEffect(() => {
    if (autoSelect && !value && only && query.isSuccess) onChange(only, revision);
  }, [autoSelect, value, only, revision, query.isSuccess, onChange]);
  return <div className="office-field-wide">
    <OfficeField label="搜索办理事项"><input type="search" value={keyword} maxLength={100} onChange={event => { setKeyword(event.target.value); setCursors([""]); }} onKeyDown={event => { if (event.key === "Enter") event.preventDefault(); }}/></OfficeField>
    <OfficeField label={supply ? "物品组与保管分工" : room ? "会议室办理分工" : "办理事项"}><select name="serviceId" value={value} disabled={query.isPending} onChange={event => onChange(event.target.value, options.find(row => row.key === event.target.value)?.revision)}>
      <option value="">{query.isPending ? "正在读取分工…" : "请选择办理分工"}</option>
      {value && !selected && <option value={value}>原分工不可用，请重新选择</option>}
      {options.map(row => <option key={row.key} value={row.key} disabled={!row.isActive}>{row.name}{!row.isActive ? "（停用）" : ""} · {row.handlerNames}</option>)}
    </select></OfficeField>
    {(cursors.length > 1 || query.data?.nextCursor) && <div className="office-card-actions"><Button type="button" disabled={cursors.length === 1 || query.isFetching} onClick={() => setCursors(values => values.slice(0,-1))}>上一页事项</Button><Button type="button" disabled={!query.data?.nextCursor || query.isFetching} onClick={() => setCursors(values => [...values, query.data!.nextCursor])}>下一页事项</Button></div>}
    {selected && <p className="office-muted" aria-live="polite">办理人员：{selected.handlerNames || "请联系管理员核对"}</p>}
    {query.isError ? <InlineNotice tone="error" action={<Button type="button" onClick={() => void query.refetch()}>重试</Button>}>{readApiError(query.error)}</InlineNotice> : !query.isPending && available.length === 0 && <InlineNotice tone="warning">{keyword ? "没有匹配的办理事项，请调整搜索词。" : "当前类别没有启用的办理事项，可先保存草稿。请管理员在“流程管理 → 办理分工”中配置。"}</InlineNotice>}
  </div>;
}
