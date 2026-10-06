import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OfficeHandlingService } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeField } from "../office/OfficeUi.tsx";

export function HandlingServicePicker({ client, user, category, value, onChange }: { client: ExportDocManagerApiClient; user: ApiUserDto; category: OfficeHandlingService["category"]; value: string; onChange: (key: string) => void }) {
  const supply = category === "Supply";
  const room = category === "Room";
  const directory = supply ? "supply" : room ? "room" : "general";
  const query = useQuery({ queryKey: ["office", "handling-services", user.id, user.companyScope, directory], queryFn: ({ signal }) => supply ? client.listSupplyHandlingServices({ signal }) : room ? client.listRoomHandlingServices({ signal }) : client.listGeneralHandlingServices({ signal }) });
  const options = query.data?.items.filter(row => row.category === category && (row.isActive || row.key === value)) ?? [];
  const selected = options.find(row => row.key === value);
  return <div className="office-field-wide">
    <OfficeField label={supply ? "物品组与保管分工" : room ? "会议室办理分工" : "具体印章／办理事项"}><select name="handlingKey" value={value} disabled={query.isPending} onChange={event => onChange(event.target.value)}>
      <option value="">{query.isPending ? "正在读取分工…" : "请选择办理分工"}</option>
      {value && !selected && <option value={value}>原分工不可用，请重新选择</option>}
      {options.map(row => <option key={row.key} value={row.key} disabled={!row.isActive}>{row.name}{!row.isActive ? "（停用）" : ""} · {row.handlerNames}</option>)}
    </select></OfficeField>
    {selected && <p className="office-muted">办理人员：{selected.handlerNames || "请联系管理员核对"}；任一位办理即可，审批仍独立执行。</p>}
    {query.isError ? <InlineNotice tone="error" action={<button type="button" onClick={() => void query.refetch()}>重试</button>}>{readApiError(query.error)}</InlineNotice> : !query.isPending && options.length === 0 && <p className="office-muted">尚未配置该类分工，请管理员在“审批规则与代理 → 办理分工”中设置。用印和证明申请提交前必须选择。</p>}
  </div>;
}
