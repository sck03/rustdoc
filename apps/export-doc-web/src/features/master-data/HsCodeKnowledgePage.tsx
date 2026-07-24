import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookOpen, CloudDownload, Database, Download, GraduationCap, RefreshCw, Search, ShieldCheck, Sparkles, Upload } from "lucide-react";
import { type FormEvent, useEffect, useRef, useState } from "react";
import { Link, Navigate, useParams } from "react-router-dom";
import type { ExportDocManagerApiClient, HsCodeKnowledgeExampleInput } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { getWorkspaceDeviceCapabilities, useWorkspaceDeviceMode } from "../../app/workspaceDevice.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useDebouncedValue } from "../../ui/useDebouncedValue.ts";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import { HsCodeToolsPanel } from "./HsCodeToolsPanel.tsx";
import { HsKnowledgeWorkflow } from "./HsKnowledgeWorkflow.tsx";
import { HsRemoteCandidateCard } from "./HsRemoteCandidateCard.tsx";

const primarySections = [
  ["search", "智能查询", Search],
  ["examples", "申报实例库", BookOpen],
  ["history", "历史资料学习", GraduationCap],
  ["online", "联网补充", CloudDownload],
] as const;
const maintenanceSections = [
  ["annual", "年度税则", Database],
  ["transfer", "换机迁移", Download],
] as const;
const sections = [...primarySections, ...maintenanceSections] as const;
type KnowledgeFeedback = { tone: "success" | "error" | "warning"; message: string };

export function HsCodeKnowledgePage({ client }: { client: ExportDocManagerApiClient }) {
  const { section = "search" } = useParams();
  const permission = useModulePermission("document.master-data");
  const queryClient = useQueryClient();
  const workspaceDeviceMode = useWorkspaceDeviceMode();
  const workspaceDeviceCapabilities = getWorkspaceDeviceCapabilities(workspaceDeviceMode);
  const [maintenanceOpen, setMaintenanceOpen] = useState(() => maintenanceSections.some(([key]) => key === section));
  useEffect(() => { setMaintenanceOpen(maintenanceSections.some(([key]) => key === section)); }, [section]);
  if (!sections.some(([key]) => key === section)) return <Navigate to="/master-data/hs-knowledge/search" replace />;
  return <section className="work-surface hs-knowledge-surface">
    <HsKnowledgeWorkflow activeSection={section}/>
    <nav className="hs-knowledge-nav" aria-label="HS知识中心功能">
      {primarySections.map(([key, label, Icon]) => <Link key={key} className={section === key ? "active" : ""} to={`/master-data/hs-knowledge/${key}`}><Icon size={18}/><span>{label}</span></Link>)}
      <details className="hs-knowledge-maintenance-nav" open={maintenanceOpen} onToggle={(event) => setMaintenanceOpen(event.currentTarget.open)}>
        <summary><Database size={18}/><span>高级维护</span></summary>
        <div>
          {maintenanceSections.map(([key, label, Icon]) => <Link key={key} className={section === key ? "active" : ""} to={`/master-data/hs-knowledge/${key}`}><Icon size={18}/><span>{label}</span></Link>)}
        </div>
      </details>
    </nav>
    {!permission.canOperate ? <PermissionNotice>当前权限只允许查询和查看；导入、确认和编辑操作已隐藏。</PermissionNotice> : null}
    {section === "search" ? <KnowledgeSearch client={client} canOperate={permission.canOperate}/> : null}
    {section === "examples" ? <ExampleLibrary client={client} canOperate={permission.canOperate && workspaceDeviceMode !== "phone"} canManage={permission.canManage && workspaceDeviceMode !== "phone"} canBatchManage={permission.canManage && workspaceDeviceCapabilities.canUseBatchOperations}/> : null}
    {section === "history" ? <HistoryLearning client={client} canOperate={permission.canOperate}/> : null}
    {section === "transfer" ? <><WorkspaceDeviceNotice mode={workspaceDeviceMode} phone="知识库迁移属于完整导入导出操作，请使用桌面端。" tablet="知识库迁移属于完整导入导出操作，请使用桌面端。"/><KnowledgeTransfer client={client} canOperate={permission.canOperate && workspaceDeviceCapabilities.canImportExport}/></> : null}
    {section === "annual" ? <div className="knowledge-task-card knowledge-tool-card"><WorkspaceDeviceNotice mode={workspaceDeviceMode} phone="年度税则导入和版本维护请使用桌面端；手机端可继续查询当前有效编码。" tablet="年度税则导入和版本维护请使用桌面端；平板端可继续查询和现场确认。"/><p className="knowledge-task-lead">只有年度税则导入和可信知识库迁移可以建立当前有效编码；每条有效编码都必须带来源、年度和验证时间。</p><HsCodeToolsPanel mode="import" client={client} disabled={!permission.canOperate || !workspaceDeviceCapabilities.canImportExport} keyword="" onLocalDataChanged={async () => { await queryClient.invalidateQueries({ queryKey: ["hs-knowledge"] }); }}/></div> : null}
    {section === "online" ? <div className="knowledge-task-card knowledge-tool-card"><WorkspaceDeviceNotice mode={workspaceDeviceMode} phone="可联网搜索并逐条审核候选；批量审核请使用桌面端。" tablet="可联网搜索并逐条审核候选；批量审核请使用桌面端。"/><p className="knowledge-task-lead">联网实例只进入待审核候选池。选择已验证年度税则中的有效编码后，才会加入公司的申报实例库。</p><HsCodeToolsPanel mode="remote" client={client} disabled={!permission.canOperate} keyword="" onLocalDataChanged={async () => undefined} onRemoteCandidatesChanged={async () => { await queryClient.invalidateQueries({ queryKey: ["hs-remote-candidates"] }); }}/><RemoteCandidateReview client={client} canOperate={permission.canOperate} canBatchOperate={permission.canOperate && workspaceDeviceCapabilities.canUseBatchOperations}/></div> : null}
  </section>;
}

function KnowledgeSearch({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate: boolean }) {
  const [draft, setDraft] = useState("");
  const [query, setQuery] = useState("");
  const [feedback, setFeedback] = useState<KnowledgeFeedback | null>(null);
  const search = useQuery({ queryKey: ["hs-knowledge", "search", query], queryFn: () => client.searchHsCodeKnowledge({ query }), enabled: !!query });
  const feedbackMutation = useMutation({
    mutationFn: (value: { code: string; accepted: boolean; name: string; specification: string }) => client.recordHsCodeKnowledgeFeedback({ body: { queryText: query, productName: value.name, specification: value.specification, candidateCode: value.code, accepted: value.accepted } }),
    onSuccess: (_, value) => { setFeedback({ tone: "success", message: value.accepted ? "已确认适用，本次选择已加入本地学习记录。" : "已记录为不适用。" }); void search.refetch(); },
    onError: error => setFeedback({ tone: "error", message: readApiError(error) }),
  });
  function submit(event: FormEvent) { event.preventDefault(); if (draft.trim()) { setFeedback(null); setQuery(draft.trim()); } }
  return <div className="knowledge-task-card knowledge-search-card">
    <div className="knowledge-intro"><Sparkles size={24}/><div><h2>像普通商品名称一样查询</h2><p>输入品名、材质、用途或规格，系统会优先展示已审核实例和当前年度税则。</p></div></div>
    <form className="knowledge-search-form" onSubmit={submit}><input value={draft} onChange={event => setDraft(event.target.value)} placeholder="例如：棉制针织男式圆领短袖T恤衫"/><button className="command-button" type="submit">查询本地知识库</button></form>
    {feedback ? <InlineNotice tone={feedback.tone}>{feedback.message}</InlineNotice> : null}
    {search.isError ? <InlineNotice tone="error" title="智能查询失败">{readApiError(search.error)}</InlineNotice> : null}
    {search.isFetching && query ? <PageState tone="loading" title="正在检索本地申报实例" description="系统正在匹配当前有效税则和已审核经验。" /> : null}
    {search.data ? <>
      <p className="muted-text">{search.data.message}</p>
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
          <div className="knowledge-score"><b>{item.score}</b><span>匹配分</span><small>{item.exampleCount} 条实例</small>{canOperate ? <><button type="button" disabled={!item.canUse || feedbackMutation.isPending} onClick={() => feedbackMutation.mutate({ code: item.currentCode, accepted: true, name: item.name, specification: item.specification })}>确认适用</button><button className="text-button" type="button" disabled={feedbackMutation.isPending} onClick={() => feedbackMutation.mutate({ code: item.currentCode || item.rawCode, accepted: false, name: item.name, specification: item.specification })}>不适用</button></> : null}</div>
      </article>)}</div>
      {search.data.items.length === 0 ? <PageState title="没有匹配的正式实例" description="可到“联网补充”获取候选，确认当前有效编码后再加入实例库。" /> : null}
    </> : null}
  </div>;
}

function ExampleLibrary({ client, canOperate, canManage, canBatchManage }: { client: ExportDocManagerApiClient; canOperate: boolean; canManage: boolean; canBatchManage: boolean }) {
  const queryClient = useQueryClient();
  const requestConfirmation = useConfirmation();
  const [keyword, setKeyword] = useState(""); const debouncedKeyword = useDebouncedValue(keyword); const [pageNumber, setPageNumber] = useState(1); const [pageSize, setPageSize] = useState(30);
  const [editing, setEditing] = useState<HsCodeKnowledgeExampleInput | null>(null); const [selected, setSelected] = useState<Set<number>>(new Set()); const [feedback, setFeedback] = useState<KnowledgeFeedback | null>(null);
  const data = useQuery({ queryKey: ["hs-knowledge", "examples", debouncedKeyword, pageNumber, pageSize], queryFn: () => client.listHsCodeKnowledgeExamples({ keyword: debouncedKeyword, pageNumber, pageSize }), placeholderData: keepPreviousData });
  const invalidate = async () => { setSelected(new Set()); await queryClient.invalidateQueries({ queryKey: ["hs-knowledge", "examples"] }); };
  const save = useMutation({ mutationFn: (value: HsCodeKnowledgeExampleInput) => client.saveHsCodeKnowledgeExample({ body: value }), onSuccess: async () => { setEditing(null); setFeedback({tone:"success",message:"申报实例已保存。"}); await invalidate(); }, onError: error => setFeedback({tone:"error",message:readApiError(error)}) });
  const remove = useMutation({ mutationFn: (id: number) => client.deleteHsCodeKnowledgeExample({ id }), onSuccess: async () => { setFeedback({tone:"success",message:"申报实例已删除。"}); await invalidate(); }, onError: error => setFeedback({tone:"error",message:readApiError(error)}) });
  const batchRemove = useMutation({ mutationFn: (ids: number[]) => client.deleteHsCodeKnowledgeExamplesBatch({ body: { ids } }), onSuccess: async response => { setFeedback({tone:"success",message:response.message}); await invalidate(); }, onError: error => setFeedback({tone:"error",message:readApiError(error)}) });
  const blank: HsCodeKnowledgeExampleInput = { id: 0, rawReportedHsCode: "", resolvedCurrentHsCode: "", productName: "", specification: "", source: "Manual", sourceYear: new Date().getFullYear(), resolutionStatus: "Unresolved", isManuallyVerified: true };
  const totalPages = Math.max(1, Math.ceil((data.data?.totalCount ?? 0) / pageSize));
  useEffect(() => { setPageNumber(1); setSelected(new Set()); }, [debouncedKeyword, pageSize]);
  function toggle(id: number) { setSelected(current => { const next = new Set(current); next.has(id) ? next.delete(id) : next.add(id); return next; }); }
  async function confirmBatchDelete() {
    if (!await requestConfirmation({
      title: "批量删除申报实例",
      description: `确定删除选中的 ${selected.size} 条申报实例吗？`,
      details: ["删除后这些经验不会再参与智能查询。", "已经生成的历史业务单据不会被修改。"],
      confirmLabel: `删除 ${selected.size} 条`,
      tone: "danger",
    })) return;
    batchRemove.mutate([...selected]);
  }
  async function confirmDelete(id: number, productName: string) {
    if (!await requestConfirmation({
      title: "删除申报实例",
      description: `确定删除“${productName}”的申报实例吗？`,
      details: ["删除后该经验不会再参与智能查询。"],
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    remove.mutate(id);
  }
  return <div className="knowledge-task-card"><div className="section-heading"><div><h2>申报实例库</h2><p>只保存已经人工确认、可以参与智能查询的公司经验。</p></div>{canOperate ? <button className="command-button" type="button" onClick={() => setEditing(blank)}>新增实例</button> : null}</div>
    <div className="knowledge-list-toolbar"><input className="knowledge-filter" aria-label="搜索申报实例" value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="搜索名称、规格或HS编码"/>{canBatchManage && selected.size ? <button className="secondary-button danger-button" type="button" onClick={() => void confirmBatchDelete()}>批量删除（{selected.size}）</button> : null}</div>
    {feedback ? <InlineNotice tone={feedback.tone}>{feedback.message}</InlineNotice> : null}
    {editing ? <form className="knowledge-editor" onSubmit={event => { event.preventDefault(); save.mutate(editing); }}><label>商品名称<input required value={editing.productName} onChange={event => setEditing({...editing, productName:event.target.value})}/></label><label>历史/原始编码<input required value={editing.rawReportedHsCode} onChange={event => setEditing({...editing, rawReportedHsCode:event.target.value})}/></label><label>当前有效编码<input value={editing.resolvedCurrentHsCode} onChange={event => setEditing({...editing, resolvedCurrentHsCode:event.target.value})}/></label><label>来源年份<input type="number" value={editing.sourceYear ?? ""} onChange={event => setEditing({...editing, sourceYear:event.target.value ? Number(event.target.value) : undefined})}/></label><label className="wide">规格与申报要素<textarea value={editing.specification} onChange={event => setEditing({...editing, specification:event.target.value})}/></label><div className="wide form-actions"><button className="command-button" type="submit" disabled={save.isPending}>保存实例</button><button className="secondary-button" type="button" onClick={() => setEditing(null)}>取消</button></div></form> : null}
    {data.isLoading ? <PageState tone="loading" title="正在读取申报实例" description="数据量较多时系统会按页加载，不会一次载入全部记录。" /> : null}{data.isError ? <PageState tone="error" title="申报实例读取失败" description={readApiError(data.error)} /> : null}
    <div className="knowledge-table">{data.data?.items.map(item => <article key={item.id}>{canBatchManage ? <input type="checkbox" aria-label={`选择 ${item.productName}`} checked={selected.has(item.id)} onChange={() => toggle(item.id)}/> : null}<div><strong>{item.productName}</strong><span>{item.rawReportedHsCode}{item.resolvedCurrentHsCode && item.resolvedCurrentHsCode !== item.rawReportedHsCode ? ` → ${item.resolvedCurrentHsCode}` : ""}</span><p>{item.specification || "暂无规格说明"}</p><small>{item.source}{item.sourceYear ? ` · ${item.sourceYear}年` : ""}</small></div><div><span className="status-pill">{knowledgeStatusLabel(item.resolutionStatus)}</span>{canOperate ? <button className="secondary-button" type="button" onClick={() => setEditing({...blank, ...item, resolvedCurrentHsCode:item.resolvedCurrentHsCode ?? "", specification:item.specification ?? ""})}>编辑</button> : null}{canManage ? <button className="text-button danger" type="button" onClick={() => void confirmDelete(item.id, item.productName)}>删除</button> : null}</div></article>)}</div>
    {data.data?.totalCount === 0 ? <PageState title="暂无正式申报实例" description="可从历史资料学习，或在联网候选审核后加入经过确认的公司经验。" /> : null}
    <ListPaginationControls pageNumber={pageNumber} totalPages={totalPages} totalCount={data.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={data.isFetching} onPageChange={setPageNumber} onPageSizeChange={setPageSize}/>
  </div>;
}

function RemoteCandidateReview({ client, canOperate, canBatchOperate }: { client: ExportDocManagerApiClient; canOperate: boolean; canBatchOperate: boolean }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState("Pending"); const [keyword, setKeyword] = useState(""); const debouncedKeyword = useDebouncedValue(keyword); const [pageNumber, setPageNumber] = useState(1); const [pageSize, setPageSize] = useState(30);
  const [selected, setSelected] = useState<Set<number>>(new Set()); const [drafts, setDrafts] = useState<Record<number,string>>({}); const [feedback, setFeedback] = useState<KnowledgeFeedback | null>(null);
  const candidates = useQuery({ queryKey: ["hs-remote-candidates", status, debouncedKeyword, pageNumber, pageSize], queryFn: () => client.listHsCodeRemoteCandidates({ status, keyword: debouncedKeyword, pageNumber, pageSize }), placeholderData: keepPreviousData });
  useEffect(() => { setPageNumber(1); setSelected(new Set()); }, [status, debouncedKeyword, pageSize]);
  useEffect(() => { setDrafts(current => { const next = {...current}; for (const item of candidates.data?.items ?? []) if (!(item.id in next)) next[item.id] = item.suggestedCurrentHsCode ?? ""; return next; }); }, [candidates.data]);
  const refresh = async (value: string) => { setFeedback({tone:"success",message:value}); setSelected(new Set()); await queryClient.invalidateQueries({ queryKey: ["hs-remote-candidates"] }); await queryClient.invalidateQueries({ queryKey: ["hs-knowledge", "examples"] }); };
  const review = useMutation({ mutationFn: (value: { id:number; code:string; confirmed:boolean }) => client.reviewHsCodeRemoteCandidate({ body: { id:value.id, currentCode:value.code, confirmed:value.confirmed } }), onSuccess: response => void refresh(response.message), onError: error => setFeedback({tone:"error",message:readApiError(error)}) });
  const batchReview = useMutation({ mutationFn: (items: {id:number;currentCode:string;confirmed:boolean}[]) => client.reviewHsCodeRemoteCandidatesBatch({ body: { items } }), onSuccess: response => void refresh(response.message), onError: error => setFeedback({tone:"error",message:readApiError(error)}) });
  const reset = useMutation({ mutationFn: (ids:number[]) => client.resetHsCodeRemoteCandidates({ body:{ ids } }), onSuccess: response => void refresh(response.message), onError: error => setFeedback({tone:"error",message:readApiError(error)}) });
  const totalPages = Math.max(1, Math.ceil((candidates.data?.totalCount ?? 0) / pageSize));
  const selectedItems = (candidates.data?.items ?? []).filter(item => selected.has(item.id));
  const confirmableSelectedItems = selectedItems.filter(item => drafts[item.id]?.trim());
  function confirmSelected() { const missing = selectedItems.length - confirmableSelectedItems.length; if (missing > 0) { setFeedback({tone:"warning",message:`选中的 ${missing} 条候选尚未填写当前有效编码，请补充后再批量确认。`}); return; } batchReview.mutate(confirmableSelectedItems.map(item => ({id:item.id,currentCode:drafts[item.id].trim(),confirmed:true}))); }
  function toggle(id:number) { setSelected(current => { const next=new Set(current); next.has(id)?next.delete(id):next.add(id); return next; }); }
  return <section className="remote-candidate-review"><div className="section-heading"><div><h2>联网候选审核</h2><p>待审核、已确认和已忽略记录分别保存；历史决定可以恢复为待审核。</p></div><button className="secondary-button" type="button" onClick={() => void candidates.refetch()}><RefreshCw size={16}/>刷新</button></div>
    <div className="candidate-status-tabs">{[["Pending","待审核"],["Confirmed","已确认"],["Ignored","已忽略"]].map(([key,label]) => <button type="button" className={status===key?"active":""} key={key} onClick={() => setStatus(key)}>{label}</button>)}</div>
    <div className="knowledge-list-toolbar"><input className="knowledge-filter" value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="筛选品名、规格、查询词或编码"/><div className="candidate-batch-actions">{canBatchOperate && status === "Pending" && selectedItems.length ? <><button className="secondary-button" type="button" onClick={confirmSelected}>批量确认可用编码（{selectedItems.length}）</button><button className="secondary-button" type="button" onClick={() => batchReview.mutate(selectedItems.map(item => ({id:item.id,currentCode:"",confirmed:false})))}>批量忽略</button></> : null}{canBatchOperate && status !== "Pending" && selectedItems.length ? <button className="secondary-button" type="button" onClick={() => reset.mutate(selectedItems.map(item => item.id))}>恢复待审核（{selectedItems.length}）</button> : null}</div></div>
    {feedback ? <InlineNotice tone={feedback.tone}>{feedback.message}</InlineNotice> : null}{candidates.isLoading ? <PageState tone="loading" title="正在读取联网候选" /> : null}{candidates.isError ? <InlineNotice tone="error" title="联网候选加载失败">{readApiError(candidates.error)}</InlineNotice> : null}
    <div className="knowledge-table">{candidates.data?.items.map(item => <HsRemoteCandidateCard key={item.id} client={client} candidate={item} status={status} canOperate={canOperate} allowSelection={canBatchOperate} selected={selected.has(item.id)} draft={drafts[item.id] ?? ""} isBusy={review.isPending || batchReview.isPending} onToggle={() => toggle(item.id)} onDraft={value => setDrafts(current => ({...current,[item.id]:value}))} onConfirm={code => review.mutate({id:item.id,code,confirmed:true})} onIgnore={() => review.mutate({id:item.id,code:"",confirmed:false})} onReset={() => reset.mutate([item.id])} statusLabel={knowledgeStatusLabel} formatVerifiedAt={formatVerifiedAt} />)}</div>
    {candidates.data?.totalCount === 0 ? <PageState title="当前分类暂无记录" description="联网查询获得的申报实例会进入待审核列表。" /> : null}
    <ListPaginationControls pageNumber={pageNumber} totalPages={totalPages} totalCount={candidates.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={candidates.isFetching} onPageChange={setPageNumber} onPageSizeChange={setPageSize}/>
  </section>;
}

function HistoryLearning({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate:boolean }) {
  const queryClient = useQueryClient();
  const [keyword, setKeyword] = useState("");
  const debouncedKeyword = useDebouncedValue(keyword);
  const [pageNumber, setPageNumber] = useState(1); const [pageSize, setPageSize] = useState(30);
  const [feedback, setFeedback] = useState<KnowledgeFeedback | null>(null);
  const candidates = useQuery({ queryKey: ["hs-history-candidates", debouncedKeyword, pageNumber, pageSize], queryFn: () => client.discoverHsCodeHistoryCandidates({ keyword: debouncedKeyword, pageNumber, pageSize }), placeholderData: keepPreviousData });
  useEffect(() => setPageNumber(1), [debouncedKeyword, pageSize]);
  const confirm = useMutation({
    mutationFn: (item: NonNullable<typeof candidates.data>["items"][number]) => client.saveHsCodeKnowledgeExample({ body: { id: 0, rawReportedHsCode: item.rawCode, resolvedCurrentHsCode: item.currentCode, productName: item.productName, specification: item.specification, source: `HistoryConfirmed:${item.source}`, sourceYear: new Date().getFullYear(), resolutionStatus: "ManuallyVerified", isManuallyVerified: true } }),
    onSuccess: async () => { setFeedback({tone:"success",message:"历史经验已确认并加入申报实例库。"}); await queryClient.invalidateQueries({ queryKey: ["hs-history-candidates"] }); await queryClient.invalidateQueries({ queryKey: ["hs-knowledge", "examples"] }); },
    onError: error => setFeedback({tone:"error",message:readApiError(error)}),
  });
  return <div className="knowledge-task-card">
    <div className="section-heading"><div><h2>从历史资料提取待确认经验</h2><p>扫描商品资料、历史发票和报关资料；确认后才进入正式实例库。</p></div></div>
    <input className="knowledge-filter" aria-label="筛选历史资料候选" value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="筛选商品名称、材质、品牌或HS编码" />
    {feedback ? <InlineNotice tone={feedback.tone}>{feedback.message}</InlineNotice> : null}
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
      <div className="history-candidate-actions"><span className="status-pill">{knowledgeStatusLabel(item.resolutionStatus)}</span>{canOperate ? <button className="command-button" type="button" disabled={!item.canConfirm || confirm.isPending} onClick={() => confirm.mutate(item)}>{item.canConfirm ? "确认加入" : "需先处理编码"}</button> : null}</div>
    </article>)}</div>
    {candidates.data?.totalCount === 0 ? <PageState title="没有新的待确认资料" description="已确认或重复的历史经验不会再次进入候选列表。" /> : null}
    <ListPaginationControls pageNumber={pageNumber} totalPages={Math.max(1, Math.ceil((candidates.data?.totalCount ?? 0) / pageSize))} totalCount={candidates.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={candidates.isFetching} onPageChange={setPageNumber} onPageSizeChange={setPageSize}/>
  </div>;
}

function KnowledgeTransfer({client,canOperate}:{client:ExportDocManagerApiClient;canOperate:boolean}) { const input=useRef<HTMLInputElement>(null);const[message,setMessage]=useState("");const importer=useMutation({mutationFn:(file:File)=>client.importHsCodeKnowledge({body:file}),onSuccess:value=>setMessage(value.result.message),onError:error=>setMessage(readApiError(error))});async function download(){const blob=await client.exportHsCodeKnowledge();const url=URL.createObjectURL(blob);const anchor=document.createElement("a");anchor.href=url;anchor.download=`ExportDocManager-HsLibrary-${new Date().toISOString().slice(0,10).replace(/-/g,"")}.edmhs`;anchor.click();URL.revokeObjectURL(url);}return <div className="knowledge-task-grid"><article className="knowledge-task-card"><Download size={28}/><h2>导出本机知识库</h2><p>包含已验证税则、正式申报实例、替代关系和学习记录，不包含业务单据和账号数据。</p>{canOperate?<button className="command-button" type="button" onClick={()=>void download()}>导出 .edmhs</button>:null}</article><article className="knowledge-task-card"><Upload size={28}/><h2>导入另一台电脑</h2><p>导入前校验完整性；缺少有效编码来源、年度或验证时间的知识包会被拒绝。</p><input ref={input} hidden type="file" accept=".edmhs" onChange={event=>{const file=event.target.files?.[0];if(file)importer.mutate(file);}}/>{canOperate?<button className="command-button" type="button" disabled={importer.isPending} onClick={()=>input.current?.click()}>选择知识库文件</button>:null}</article>{message?<InlineNotice tone={importer.isError?"error":"success"} className="knowledge-transfer-message">{message}</InlineNotice>:null}</div>; }

function knowledgeStatusLabel(status?:string){const labels:Record<string,string>={Active:"当前有效",SuggestedReplacement:"待核验替代",WebRecommended:"网页推荐待核验",ObsoleteMapped:"已确认替代",ObsoleteUnresolved:"未找到替代",Ambiguous:"多条替代待选",ManuallyVerified:"人工已确认",Unresolved:"待处理"};return labels[status??""]??status??"待处理";}
function formatVerifiedAt(value?:string){if(!value)return"未标明验证时间";const date=new Date(value);return Number.isNaN(date.getTime())?value:`验证于 ${date.toLocaleDateString("zh-CN")}`;}
