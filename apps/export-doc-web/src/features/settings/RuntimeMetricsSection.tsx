import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryPollingInterval } from "../../api/queryPollingPolicy.ts";
import { Button } from "../../ui/Button.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBytes } from "./settingsFormatters.ts";

export function RuntimeMetricsSection({ client }: { client: ExportDocManagerApiClient }) {
  const query = useQuery({ queryKey: ["runtime-metrics"], gcTime: 0, retry: false,
    queryFn: ({ signal }) => client.getRuntimeMetrics({ signal }),
    refetchInterval: current => queryPollingInterval(current, 10_000), refetchIntervalInBackground: false });
  if (query.isPending) return <PageState tone="loading" title="正在读取资源使用情况" />;
  if (query.isError) return <PageState tone="error" title="运行指标读取失败" description={readApiError(query.error)} action={<Button onClick={() => void query.refetch()}>重试</Button>} />;
  const { storage, jobs, http } = query.data;
  const values = [
    ["数据库连接", `${storage.leased} / ${storage.capacity}`],
    ["数据库状态", storage.failed || storage.writeCoordinatorFailed ? "异常" : "正常"],
    ["写入等待 P95", `${(storage.writeWait.p95UpperBoundMicroseconds / 1000).toFixed(1)} ms`],
    ["活动文件任务", String(jobs.active)],
    ["任务输入", formatBytes(jobs.retainedInputBytes)], ["任务输出", formatBytes(jobs.retainedOutputBytes)],
    ["文件缓冲预算", `${formatBytes(jobs.downloadMemory.reservedBytes)} / ${formatBytes(jobs.downloadMemory.capacityBytes)}`],
    ...(http ? [["HTTP 执行请求", `${http.requestCapacity - http.availableRequestSlots} / ${http.requestCapacity}`],
      ["HTTP 等待请求", `${http.queuedRequests} / ${http.queueCapacity}`],
      ["JSON 缓冲预算", `${formatBytes(http.responseMemory.reservedBytes)} / ${formatBytes(http.responseMemory.capacityBytes)}`],
      ["JSON 缓冲拒绝", String(http.responseMemory.rejections)], ["文件下载超时", String(http.downloadTimeouts)]] : []),
  ];
  return <section aria-label="运行资源指标"><div className="toolbar-actions"><Button aria-label="刷新资源使用" disabled={query.isFetching} onClick={() => void query.refetch()}>刷新</Button></div>
    <p className="section-description">显示每个服务实例的请求名额与缓冲预算，程序总内存需单独监测。</p>
    <dl className="detail-grid">{values.map(([name, value]) => <div className="detail-item" key={name}><dt>{name}</dt><dd>{value}</dd></div>)}</dl>
  </section>;
}
