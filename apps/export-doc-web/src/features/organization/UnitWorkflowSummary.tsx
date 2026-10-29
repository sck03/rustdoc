import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";

function duration(seconds: number | null | undefined): string {
  if (seconds == null) return "暂无已处理任务";
  if (seconds < 60) return "不足 1 分钟";
  if (seconds < 3600) return `${Math.round(seconds / 60)} 分钟`;
  return `${(seconds / 3600).toLocaleString("zh-CN", { maximumFractionDigits: 1 })} 小时`;
}

export function UnitWorkflowSummary({ client, user, unitId }: { client: ExportDocManagerApiClient; user: ApiUserDto; unitId?: number }) {
  const [page,setPage] = useState(1);
  const query = useQuery({ queryKey:["unit-workflow-summary",user.contextId,unitId,page],queryFn:({ signal })=>client.getUnitWorkflowSummary({ unitId,pageNumber:page,pageSize:20 },{ signal }) });
  return <section aria-label="单位审批统计"><div className="organization-heading"><h3>单位审批统计</h3><button type="button" className="text-button" disabled={query.isFetching} onClick={()=>void query.refetch()}>刷新统计</button></div><p>按当前统计授权汇总在途事项及近 30 天处理情况。包含下级单位须有明确授权。</p>
    {query.isError ? <PageState tone="error" title="统计读取失败" description={readApiError(query.error)} /> : query.isPending ? <PageState tone="loading" title="正在汇总在途任务" /> : <>
      <div className="office-table-scroll"><table><thead><tr><th>单位</th><th>在途申请</th><th>待审批任务</th><th>等待前序</th><th>逾期任务</th><th>近 30 天已处理</th><th>平均节点处理时长</th></tr></thead><tbody>{query.data.items.map(row=><tr key={row.unitId}><td>{row.companyName}</td><td>{row.requestCount}</td><td>{row.activeTaskCount}</td><td>{row.queuedTaskCount}</td><td>{row.overdueTaskCount}</td><td>{row.completedTaskCount}</td><td>{duration(row.averageProcessingSeconds)}</td></tr>)}</tbody></table></div>
      {!query.data.items.length && <p>当前范围没有经营单位。</p>}<p>处理区间：{formatBusinessDateTime(query.data.completedSince,user.businessTimeZone)} 至 {formatBusinessDateTime(query.data.asOf,user.businessTimeZone)}。仅统计实际同意、驳回或退回的任务；时长从节点激活到签署，包含非工作时间及交接前等待时间。</p>
      <div className="office-card-actions"><button type="button" disabled={page===1 || query.isFetching} onClick={()=>setPage(page-1)}>上一页</button><span>第 {page} 页 · 共 {query.data.totalCount} 个单位</span><button type="button" disabled={page*20>=query.data.totalCount || query.isFetching} onClick={()=>setPage(page+1)}>下一页</button></div>
    </>}
  </section>;
}
