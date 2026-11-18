import { ExactDecimal } from "../../ui/decimalSum.ts";
import { useMemo, useState } from "react";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { Button } from "../../ui/Button.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

export function SalesOpportunityHistory({ client, id, timeZone }: { client: ExportDocManagerApiClient; id: number; timeZone: string }) {
  const [cursors, setCursors] = useState<(number | undefined)[]>([undefined]);
  const beforeId = cursors.at(-1);
  const scope = useMemo(createRequestKey, [client]);
  const history = useQuery({ queryKey: ["sales-opportunities", "history", scope, id, beforeId], gcTime: 0,
    queryFn: ({ signal }) => client.listSalesOpportunityHistory({ id, beforeId, limit: 30 }, { signal }) });
  if (history.isPending) return <PageState tone="loading" title="正在读取商机历史" />;
  if (history.isError) return <PageState tone="error" title="历史加载失败" description={readApiError(history.error)} action={<Button onClick={() => void history.refetch()}>重试</Button>} />;
  return <section className="form-section" aria-label="报价与阶段历史">
    <ResponsiveTableFrame label="商机版本历史" mobileLayout="scroll" busy={history.isFetching}><table className="data-table"><thead><tr>
      <th>版本</th><th>类型</th><th>阶段</th><th>报价编号</th><th>金额</th><th>概率</th><th>变更说明</th><th>操作信息</th>
    </tr></thead><tbody>{history.data.items.map(item => <tr key={item.id}>
      <td>V{item.versionNumber}</td><td>{item.changeType}</td><td>{item.stage}</td><td>{item.quotationNo || "—"}</td>
      <td>{item.currency} {ExactDecimal.from(item.estimatedAmount).toFixed(2)}</td><td>{item.probabilityPercent}%</td><td>{item.changeNote || "—"}</td>
      <td>{item.changedBy}<br /><small>{formatBusinessDateTime(item.createdAt, timeZone)}</small></td>
    </tr>)}</tbody></table></ResponsiveTableFrame>
    {!history.data.items.length && <PageState title="暂无版本历史" />}
    <div className="form-actions"><Button disabled={cursors.length === 1 || history.isFetching} onClick={() => setCursors(values => values.slice(0, -1))}>上一页</Button>
      <span>第 {cursors.length} 页</span><Button disabled={!history.data.nextCursor || history.isFetching} onClick={() => setCursors(values => [...values, history.data.nextCursor!])}>下一页</Button></div>
  </section>;
}
