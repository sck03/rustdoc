import { useQuery } from "@tanstack/react-query";
import { ExternalLink } from "lucide-react";
import type { ExportDocManagerApiClient, HsCodeRemoteCandidate } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { useDebouncedValue } from "../../ui/useDebouncedValue.ts";

export function HsRemoteCandidateCard({
  client,
  candidate,
  status,
  canOperate,
  allowSelection,
  selected,
  draft,
  isBusy,
  onToggle,
  onDraft,
  onConfirm,
  onIgnore,
  onReset,
  statusLabel,
  formatVerifiedAt,
}: {
  client: ExportDocManagerApiClient;
  candidate: HsCodeRemoteCandidate;
  status: string;
  canOperate: boolean;
  allowSelection: boolean;
  selected: boolean;
  draft: string;
  isBusy: boolean;
  onToggle: () => void;
  onDraft: (value: string) => void;
  onConfirm: (code: string) => void;
  onIgnore: () => void;
  onReset: () => void;
  statusLabel: (status?: string) => string;
  formatVerifiedAt: (value?: string) => string;
}) {
  return <article className={canOperate && allowSelection ? "remote-candidate-card remote-candidate-card-selectable" : "remote-candidate-card"}>
    {canOperate && allowSelection ? <input type="checkbox" aria-label={`选择 ${candidate.productName}`} checked={selected} onChange={onToggle} /> : null}
    <div className="remote-candidate-evidence">
      <div className="remote-candidate-title"><strong>{candidate.productName}</strong><span className="status-pill">{statusLabel(candidate.resolutionStatus)}</span></div>
      <div className="remote-code-comparison">
        <span><small>网页实例编码</small><b>{candidate.rawReportedHsCode || "未提供"}</b></span>
        <i aria-hidden="true">→</i>
        <span className={candidate.suggestedCurrentHsCode ? "current-code" : "current-code unresolved"}><small>待确认当前编码</small><b>{candidate.suggestedCurrentHsCode || "需要人工选择"}</b></span>
      </div>
      <p>{candidate.specification || "暂无规格与申报要素"}</p>
      <div className="remote-candidate-meta"><small>查询词：{candidate.queryText}</small><small>网页出现 {candidate.seenCount} 次</small><small>来源：{candidate.source}</small>{candidate.reviewedAt ? <small>审核：{formatVerifiedAt(candidate.reviewedAt).replace("验证于 ", "")}</small> : null}</div>
      {candidate.sourceUrl ? <a className="knowledge-source-link" href={candidate.sourceUrl} target="_blank" rel="noreferrer">查看网页证据<ExternalLink size={13} aria-hidden="true" /></a> : null}
    </div>
    <div className="remote-candidate-actions">
      {status === "Pending" && canOperate ? <>
        <RemoteCandidatePicker client={client} candidate={candidate} draft={draft} disabled={isBusy} onDraft={onDraft} onConfirm={onConfirm} />
        <Button variant="text" disabled={isBusy} onClick={onIgnore}>忽略此实例</Button>
      </> : null}
      {status !== "Pending" && canOperate ? <Button onClick={onReset}>恢复待审核</Button> : null}
    </div>
  </article>;
}

function RemoteCandidatePicker({ client, candidate, draft, disabled, onDraft, onConfirm }: { client: ExportDocManagerApiClient; candidate: HsCodeRemoteCandidate; draft:string; disabled:boolean; onDraft:(value:string)=>void; onConfirm:(code:string)=>void }) {
  const debouncedDraft = useDebouncedValue(draft.trim(), 350);
  const lookup = useQuery({
    queryKey:["hs-active-code-picker",candidate.id,debouncedDraft],
    queryFn:({signal})=>client.listHsCodes({pageNumber:1,pageSize:30,keyword:debouncedDraft},{signal}),
    enabled:debouncedDraft.length>=4,
  });
  const activeItems=(lookup.data?.items??[]).filter(item=>item.status==="Active"&&item.sourceName&&item.effectiveYear&&item.lastVerifiedAt);
  const selected=activeItems.find(item=>(item.code||item.normalizedCode).replace(/[^0-9A-Za-z]/g,"")===draft.replace(/[^0-9A-Za-z]/g,""));
  return <div className="remote-candidate-picker" aria-busy={lookup.isFetching}><input aria-label="选择当前有效HS编码" list={`active-hs-codes-${candidate.id}`} value={draft} onChange={event=>onDraft(event.target.value)} placeholder="输入有效编码检索"/><datalist id={`active-hs-codes-${candidate.id}`}>{activeItems.map(item=><option key={item.normalizedCode} value={item.code||item.normalizedCode}>{item.name} · {item.effectiveYear}年</option>)}</datalist><Button variant="primary" disabled={disabled||!selected} onClick={()=>selected&&onConfirm(selected.code||selected.normalizedCode)}>{lookup.isFetching?"检索中":selected?"确认加入":"选择有效编码"}</Button>{lookup.isError?<small className="remote-candidate-picker-error">有效编码检索失败，请稍后重试。</small>:null}</div>;
}
