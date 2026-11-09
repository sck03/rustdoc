import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import "../../styles/features/hs-knowledge.css";
import { ShieldCheck, Sparkles } from "lucide-react";
import { type FormEvent, useEffect, useState } from "react";
import { Navigate, useParams } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { useWorkspaceDeviceProfile } from "../../app/workspaceDevice.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { dateInputToApiDate, readApiError } from "../../ui/formUtils.ts";
import { useDebouncedValue } from "../../ui/useDebouncedValue.ts";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import { HsKnowledgeTransfer } from "./HsKnowledgeTransfer.tsx";
import { Button } from "../../ui/Button.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { HsCodeToolsPanel } from "./HsCodeToolsPanel.tsx";
import { HsKnowledgeWorkflow } from "./HsKnowledgeWorkflow.tsx";
import { HsKnowledgeNavigation, hsKnowledgeSections } from "./HsKnowledgeNavigation.tsx";
import { ExampleLibrary } from "./HsExampleLibrary.tsx";
import { RemoteCandidateReview } from "./HsRemoteCandidateReview.tsx";
import {
  formatHsKnowledgeStatus as knowledgeStatusLabel,
  formatHsKnowledgeVerifiedAt as formatVerifiedAt,
} from "./hsKnowledgeDisplay.ts";

type KnowledgeFeedback = { tone: "success" | "error" | "warning"; message: string };

export function HsCodeKnowledgePage({ businessDate, client }: { businessDate: string; client: ExportDocManagerApiClient }) {
  const { section = "search" } = useParams();
  const permission = useModulePermission("document.hs-knowledge");
  const queryClient = useQueryClient();
  const workspaceDeviceProfile = useWorkspaceDeviceProfile();
  const workspaceDeviceMode = workspaceDeviceProfile.mode;
  const workspaceDeviceCapabilities = workspaceDeviceProfile.capabilities;
  const normalizedBusinessDate = dateInputToApiDate(businessDate);
  if (!normalizedBusinessDate) throw new Error("服务端业务日期无效。");
  const businessYear = Number(normalizedBusinessDate.slice(0, 4));
  const selectedSection = hsKnowledgeSections.find((item) => item.key === section);
  if (!selectedSection || section === "catalog") return <Navigate to="/master-data/hs-knowledge/search" replace />;
  if (!permission.canManage && "manage" in selectedSection) return <Navigate to="/master-data/hs-knowledge/search" replace />;
  return <ListWorkspace className="hs-knowledge-surface">
    <HsKnowledgeNavigation activeSection={section} canManage={permission.canManage} />
    <HsKnowledgeWorkflow />
    {!permission.canManage ? <PermissionNotice>当前权限只允许查询和查看；共享知识库导入、确认和编辑需要 HS 编码管理权限。</PermissionNotice> : null}
    {section === "search" ? <KnowledgeSearch client={client} canOperate={permission.canOperate}/> : null}
    {section === "examples" ? <ExampleLibrary businessYear={businessYear} client={client} canOperate={permission.canManage && workspaceDeviceMode !== "phone"} canManage={permission.canManage && workspaceDeviceMode !== "phone"} canBatchManage={permission.canManage && workspaceDeviceCapabilities.canUseBatchOperations}/> : null}
    {section === "history" ? <HistoryLearning businessYear={businessYear} client={client} canOperate={permission.canManage}/> : null}
    {section === "transfer" ? <>{!workspaceDeviceCapabilities.canImportExport ? <WorkspaceDeviceNotice mode={workspaceDeviceMode} phone="知识库迁移属于完整导入导出操作，请使用带鼠标或触控板的电脑。" tablet="连接鼠标或触控板后可在当前设备完成知识库迁移。"/> : null}<HsKnowledgeTransfer client={client} canImport={permission.canManage && workspaceDeviceCapabilities.canImportExport} canExport={permission.canManage && workspaceDeviceCapabilities.canImportExport}/></> : null}
    {section === "annual" ? <div className="knowledge-task-card knowledge-tool-card">{!workspaceDeviceCapabilities.canImportExport ? <WorkspaceDeviceNotice mode={workspaceDeviceMode} phone="年度税则导入和版本维护请使用带鼠标或触控板的电脑；手机端可继续查询当前有效编码。" tablet="连接鼠标或触控板后可在当前设备导入年度税则；当前仍可查询和现场确认。"/> : null}<p className="knowledge-task-lead">只有年度税则导入和可信知识库迁移可以建立当前有效编码；每条有效编码都必须带来源、年度和验证时间。</p><HsCodeToolsPanel businessYear={businessYear} mode="import" client={client} disabled={!permission.canManage || !workspaceDeviceCapabilities.canImportExport} keyword="" onLocalDataChanged={async () => { await queryClient.invalidateQueries({ queryKey: ["hs-knowledge"] }); }}/></div> : null}
    {section === "online" ? <div className="knowledge-task-card knowledge-tool-card"><WorkspaceDeviceNotice mode={workspaceDeviceMode} phone="可联网搜索并逐条审核候选；批量审核请使用桌面端。" tablet="可联网搜索并逐条审核候选；批量审核请使用桌面端。"/><p className="knowledge-task-lead">联网实例只进入待审核候选池。选择已验证年度税则中的有效编码后，才会加入公司的申报实例库。</p><HsCodeToolsPanel businessYear={businessYear} mode="remote" client={client} disabled={!permission.canManage} keyword="" onLocalDataChanged={async () => undefined} onRemoteCandidatesChanged={async () => { await queryClient.invalidateQueries({ queryKey: ["hs-remote-candidates"] }); }}/><RemoteCandidateReview client={client} canOperate={permission.canManage} canBatchOperate={permission.canManage && workspaceDeviceCapabilities.canUseBatchOperations}/></div> : null}
  </ListWorkspace>;
}

function KnowledgeSearch({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate: boolean }) {
  const [draft, setDraft] = useState("");
  const [query, setQuery] = useState("");
  const [feedback, setFeedback] = useState<KnowledgeFeedback | null>(null);
  const search = useQuery({ queryKey: ["hs-knowledge", "search", query], queryFn: ({ signal }) => client.searchHsCodeKnowledge({ query }, { signal }), enabled: !!query });
  const feedbackMutation = useMutation({
    mutationFn: (value: { code: string; accepted: boolean; name: string; specification: string }) => client.recordHsCodeKnowledgeFeedback({ body: { queryText: query, productName: value.name, specification: value.specification, candidateCode: value.code, accepted: value.accepted } }),
    onSuccess: (_, value) => { setFeedback({ tone: "success", message: value.accepted ? "已记录：当前商品与该候选编码匹配，用于优化后续推荐。" : "已记录：当前商品与该候选编码不匹配，用于优化后续推荐。" }); void search.refetch(); },
    onError: error => setFeedback({ tone: "error", message: readApiError(error) }),
  });
  function submit(event: FormEvent) { event.preventDefault(); if (draft.trim()) { setFeedback(null); setQuery(draft.trim()); } }
  return <div className="knowledge-task-card knowledge-search-card">
    <div className="knowledge-intro"><Sparkles size={24}/><div><h2>像普通商品名称一样查询</h2><p>输入品名、材质、用途或规格，系统会优先展示已审核实例和当前年度税则。</p></div></div>
    <form className="knowledge-search-form" onSubmit={submit}><input value={draft} maxLength={500} onChange={event => setDraft(event.target.value)} placeholder="例如：棉制针织男式圆领短袖T恤衫"/><button className="command-button" type="submit">查询本地知识库</button></form>
    {feedback ? <InlineNotice tone={feedback.tone}>{feedback.message}</InlineNotice> : null}
    {search.isError ? <InlineNotice tone="error" title="智能查询失败">{readApiError(search.error)}</InlineNotice> : null}
    {search.isFetching && query ? <PageState tone="loading" title="正在检索本地申报实例" description="系统正在匹配当前有效税则和已审核经验。" /> : null}
    {search.data ? <>
      <p className="muted-text">{search.data.message}</p>
      {canOperate ? <div className="knowledge-feedback-explainer" role="note"><div><strong>推荐反馈</strong><span>下面的按钮只表示“当前商品与候选编码是否匹配”，不是 HS 编码本身同时处于适用和不适用状态。</span></div><small>不确定时可以不操作，不影响查询和查看。</small></div> : null}
      <div className="knowledge-result-list">{search.data.items.map(item => <article key={`${item.currentCode}-${item.rawCode}`} className={`knowledge-result ${item.canUse ? "usable" : "warning"}`}>
        <div>
          <span className="status-pill">{knowledgeStatusLabel(item.resolutionStatus)}</span>
          <h3>{item.name || item.standardName}</h3>
          <strong>{item.currentCode || item.rawCode}</strong>
          {item.currentCode && item.rawCode !== item.currentCode ? <small>历史编码 {item.rawCode}</small> : null}
          <p>{item.specification || item.standardName}</p>
          <div className="knowledge-provenance"><ShieldCheck size={14}/><span>{item.standardSource || "来源未标明"}</span><span>{item.effectiveYear ? `${item.effectiveYear} 年税则` : "年度未标明"}</span><span>{formatVerifiedAt(item.lastVerifiedAt)}</span></div>
          <div className="knowledge-match-notes">{item.matchReasons.map(reason => <small key={reason}>{reason}</small>)}{item.conflictWarnings.map(warning => <small className="conflict" key={warning}>{warning}</small>)}</div>
        </div>
          <div className="knowledge-score"><b>{item.score}</b><span>匹配分</span><small>{item.exampleCount} 条实例</small>{canOperate ? <div className="knowledge-feedback-actions" aria-label="对当前推荐结果反馈"><span>当前商品是否匹配？</span><button type="button" title="确认当前商品与此编码匹配" disabled={!item.canUse || feedbackMutation.isPending} onClick={() => feedbackMutation.mutate({ code: item.currentCode, accepted: true, name: item.name, specification: item.specification })}>确认匹配</button><button className="text-button" type="button" title="标记当前商品与此编码不匹配" disabled={feedbackMutation.isPending} onClick={() => feedbackMutation.mutate({ code: item.currentCode || item.rawCode, accepted: false, name: item.name, specification: item.specification })}>标记不匹配</button></div> : null}</div>
      </article>)}</div>
      {search.data.items.length === 0 ? <PageState title="没有匹配的正式实例" description="可到“联网补充”获取候选，确认当前有效编码后再加入实例库。" /> : null}
    </> : null}
  </div>;
}

function HistoryLearning({ businessYear, client, canOperate }: { businessYear: number; client: ExportDocManagerApiClient; canOperate:boolean }) {
  const queryClient = useQueryClient();
  const [keyword, setKeyword] = useState("");
  const debouncedKeyword = useDebouncedValue(keyword);
  const [pageNumber, setPageNumber] = useState(1); const [pageSize, setPageSize] = useState(30);
  const [feedback, setFeedback] = useState<KnowledgeFeedback | null>(null);
  const candidates = useQuery({ queryKey: ["hs-history-candidates", debouncedKeyword, pageNumber, pageSize], queryFn: ({ signal }) => client.discoverHsCodeHistoryCandidates({ keyword: debouncedKeyword, pageNumber, pageSize }, { signal }), placeholderData: keepPreviousData });
  useServerPageNumber(candidates, pageNumber, setPageNumber);
  useEffect(() => setPageNumber(1), [debouncedKeyword, pageSize]);
  const confirm = useMutation({
    mutationFn: (item: NonNullable<typeof candidates.data>["items"][number]) => client.saveHsCodeKnowledgeExample({ body: { id: 0, rawReportedHsCode: item.rawCode, resolvedCurrentHsCode: item.currentCode, productName: item.productName, specification: item.specification, source: `HistoryConfirmed:${item.source}`, sourceYear: businessYear, resolutionStatus: "ManuallyVerified", isManuallyVerified: true } }),
    onSuccess: async () => { setFeedback({tone:"success",message:"历史经验已确认并加入申报实例库。"}); await queryClient.invalidateQueries({ queryKey: ["hs-history-candidates"] }); await queryClient.invalidateQueries({ queryKey: ["hs-knowledge", "examples"] }); },
    onError: error => setFeedback({tone:"error",message:readApiError(error)}),
  });
  return <div className="knowledge-task-card">
    <div className="section-heading"><div><h2>从历史资料提取待确认经验</h2><p>扫描商品资料、历史发票和报关资料；确认后才进入正式实例库。</p></div></div>
    <input className="knowledge-filter" aria-label="筛选历史资料候选" value={keyword} maxLength={200} onChange={event => setKeyword(event.target.value)} placeholder="筛选商品名称、材质、品牌或HS编码" />
     {feedback ? <InlineNotice tone={feedback.tone}>{feedback.message}</InlineNotice> : null}
     {candidates.data?.notice ? <InlineNotice tone="warning" title="历史资料分批分析">{candidates.data.notice}</InlineNotice> : null}
     {candidates.isLoading ? <PageState tone="loading" title="正在分析历史资料" description="系统正在扫描商品、历史发票和已保存申报经验。" /> : null}
    {candidates.isError ? <PageState tone="error" title="历史资料分析失败" description={readApiError(candidates.error)} /> : null}
    <div className="knowledge-table history-candidate-list">{candidates.data?.items.map(item => <article className="history-candidate-card" key={item.fingerprint}>
      <div className="history-candidate-main">
        <div className="history-candidate-heading">
          <strong>{item.productName}</strong>
          <code>{item.rawCode}{item.currentCode && item.currentCode !== item.rawCode ? ` → ${item.currentCode}` : ""}</code>
        </div>
        <p className="history-candidate-specification">{item.specification || "暂无规格"}</p>
        {item.variantCount > 0 ? <div className="history-candidate-variants"><small>历史款式</small><span>{item.variantSamples.join("、")}{item.variantCount > item.variantSamples.length ? ` 等 ${item.variantCount} 个` : ""}</span></div> : null}
        <div className="history-candidate-meta"><span>{item.source}</span><span>历史资料 {item.sourceCount} 条</span>{item.variantCount > 0 ? <span>{item.variantCount} 个不同款式</span> : null}</div>
      </div>
      <div className="history-candidate-actions"><span className="status-pill">{knowledgeStatusLabel(item.resolutionStatus)}</span>{canOperate ? <Button disabled={!item.canConfirm || confirm.isPending} onClick={() => confirm.mutate(item)}>{item.canConfirm ? "确认加入" : "需先处理编码"}</Button> : null}</div>
    </article>)}</div>
    {candidates.data?.totalCount === 0 ? <PageState title="没有新的待确认资料" description="已确认或重复的历史经验不会再次进入候选列表。" /> : null}
    <Pagination pageNumber={pageNumber} totalPages={Math.max(1, Math.ceil((candidates.data?.totalCount ?? 0) / pageSize))} totalCount={candidates.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={candidates.isFetching} onPageChange={setPageNumber} onPageSizeChange={setPageSize}/>
  </div>;
}
