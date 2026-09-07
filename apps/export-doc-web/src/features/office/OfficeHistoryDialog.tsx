import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { officeHistoryLabels, type OfficeKind } from "./officeModel.ts";
import { useOfficeHistory } from "./useOfficeData.ts";
import { OfficeDialog, OfficePager, OfficeQueryState } from "./OfficeUi.tsx";

export function OfficeHistoryDialog({ client, user, kind, id, title, onClose }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; kind: OfficeKind | "stock"; id: number; title: string; onClose: () => void;
}) {
  const { paging, query } = useOfficeHistory(client, user, kind, id);
  return <OfficeDialog title={title} onClose={onClose}>
    <OfficeQueryState query={query} emptyTitle="暂无记录" />
    {!query.isError && <ol className="office-history">{query.data?.items.map((entry) => <li key={entry.id}>
      <div className="office-card-heading"><strong>{officeHistoryLabels["kind" in entry ? entry.kind : entry.action] ?? ("kind" in entry ? entry.kind : entry.action)}</strong>
        <time dateTime={entry.createdAt}>{formatBusinessDateTime(entry.createdAt, user.businessTimeZone)}</time></div>
      {"quantityDelta" in entry && <p>变动 {entry.quantityDelta > 0 ? "+" : ""}{entry.quantityDelta} · 变动后库存 {entry.stockAfter}</p>}
      {"quantity" in entry && entry.quantity > 0 && <p>本次数量：{entry.quantity}</p>}
      <p>{entry.actorName}{entry.note ? ` · ${entry.note}` : ""}</p>
    </li>)}</ol>}
    <OfficePager page={query.data} paging={paging} busy={query.isFetching} />
  </OfficeDialog>;
}
