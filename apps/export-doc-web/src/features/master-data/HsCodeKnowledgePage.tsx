import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookOpen, CloudDownload, Database, Download, ExternalLink, GraduationCap, RefreshCw, Search, ShieldCheck, Sparkles, Upload } from "lucide-react";
import { type FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { Link, Navigate, useParams } from "react-router-dom";
import type { ExportDocManagerApiClient, HsCodeKnowledgeExampleInput, HsCodeRemoteCandidate } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { HsCodeToolsPanel } from "./HsCodeToolsPanel.tsx";

const sections = [
  ["search", "智能查询", Search], ["examples", "申报实例库", BookOpen], ["history", "历史资料学习", GraduationCap],
  ["annual", "年度税则", Database], ["transfer", "换机迁移", Download], ["online", "联网补充", CloudDownload],
] as const;

export function HsCodeKnowledgePage({ client }: { client: ExportDocManagerApiClient }) {
  const { section = "search" } = useParams();
  const permission = useModulePermission("document.master-data");
  const queryClient = useQueryClient();
  if (!sections.some(([key]) => key === section)) return <Navigate to="/master-data/hs-knowledge/search" replace />;
  return <section className="work-surface hs-knowledge-surface">
    <nav className="hs-knowledge-nav" aria-label="HS知识中心功能">
      {sections.map(([key, label, Icon]) => <Link key={key} className={section === key ? "active" : ""} to={`/master-data/hs-knowledge/${key}`}><Icon size={18}/><span>{label}</span></Link>)}
    </nav>
    {!permission.canOperate ? <div className="permission-readonly-notice">当前权限只允许查询和查看；导入、确认和编辑操作已隐藏。</div> : null}
    {section === "search" ? <KnowledgeSearch client={client} canOperate={permission.canOperate}/> : null}
    {section === "examples" ? <ExampleLibrary client={client} canOperate={permission.canOperate} canManage={permission.canManage}/> : null}
    {section === "history" ? <HistoryLearning client={client} canOperate={permission.canOperate}/> : null}
    {section === "transfer" ? <KnowledgeTransfer client={client} canOperate={permission.canOperate}/> : null}
    {section === "annual" ? <div className="knowledge-task-card knowledge-tool-card"><p className="knowledge-task-lead">只有年度税则导入和可信知识库迁移可以建立当前有效编码；每条有效编码都必须带来源、年度和验证时间。</p><HsCodeToolsPanel mode="import" client={client} disabled={!permission.canOperate} keyword="" onLocalDataChanged={async () => { await queryClient.invalidateQueries({ queryKey: ["hs-knowledge"] }); }}/></div> : null}
    {section === "online" ? <div className="knowledge-task-card knowledge-tool-card"><p className="knowledge-task-lead">联网实例只进入待审核候选池。选择已验证年度税则中的有效编码后，才会加入公司的申报实例库。</p><HsCodeToolsPanel mode="remote" client={client} disabled={!permission.canOperate} keyword="" onLocalDataChanged={async () => undefined} onRemoteCandidatesChanged={async () => { await queryClient.invalidateQueries({ queryKey: ["hs-remote-candidates"] }); }}/><RemoteCandidateReview client={client} canOperate={permission.canOperate}/></div> : null}
  </section>;
}

function KnowledgeSearch({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate: boolean }) {
  const [draft, setDraft] = useState("");
  const [query, setQuery] = useState("");
  const [message, setMessage] = useState("");
  const search = useQuery({ queryKey: ["hs-knowledge", "search", query], queryFn: () => client.searchHsCodeKnowledge({ query }), enabled: !!query });
  const feedback = useMutation({
    mutationFn: (value: { code: string; accepted: boolean; name: string; specification: string }) => client.recordHsCodeKnowledgeFeedback({ body: { queryText: query, productName: value.name, specification: value.specification, candidateCode: value.code, accepted: value.accepted } }),
    onSuccess: (_, value) => { setMessage(value.accepted ? "已确认适用，本次选择已加入本地学习记录。" : "已记录为不适用。"); void search.refetch(); },
    onError: error => setMessage(readApiError(error)),
  });
  function submit(event: FormEvent) { event.preventDefault(); if (draft.trim()) { setMessage(""); setQuery(draft.trim()); } }
  return <div className="knowledge-task-card knowledge-search-card">
    <div className="knowledge-intro"><Sparkles size={24}/><div><h2>像普通商品名称一样查询</h2><p>输入品名、材质、用途或规格，系统会优先展示已审核实例和当前年度税则。</p></div></div>
    <form className="knowledge-search-form" onSubmit={submit}><input value={draft} onChange={event => setDraft(event.target.value)} placeholder="例如：棉制针织男式圆领短袖T恤衫"/><button className="command-button" type="submit">查询本地知识库</button></form>
    {message ? <div className="success-alert">{message}</div> : null}
    {search.isError ? <div className="error-alert">{readApiError(search.error)}</div> : null}
    {search.isFetching && query ? <div className="empty-guidance">正在检索本地申报实例...</div> : null}
    {search.data ? <><p className="muted-text">{search.data.message}</p><div className="knowledge-result-list">{search.data.items.map(item => <article key={`${item.currentCode}-${item.rawCode}`} className={`knowledge-result ${item.canUse ? "usable" : "warning"}`}><div><span className="status-pill">{knowledgeStatusLabel(item.resolutionStatus)}</span><h3>{item.name || item.standardName}</h3><strong>{item.currentCode || item.rawCode}</strong>{item.currentCode && item.rawCode !== item.currentCode ? <small>历史编码 {item.rawCode}</small> : null}<p>{item.specification || item.standardName}</p><div className="knowledge-provenance"><ShieldCheck size={14}/><span>{item.standardSource || "来源未标明"}</span><span>{item.effectiveYear ? `${item.effectiveYear} 年税则` : "年度未标明"}</span><span>{formatVerifiedAt(item.lastVerifiedAt)}</span></div><div className="knowledge-match-notes">{item.matchReasons.map(reason => <small key={reason}>{reason}</small>)}{item.conflictWarnings.map(warning => <small className="conflict" key={warning}>{warning}</small>)}</div></div><div className="knowledge-score"><b>{item.score}</b><span>匹配分</span><small>{item.exampleCount} 条实例</small>{canOperate ? <><button disabled={!item.canUse || feedback.isPending} onClick={() => feedback.mutate({ code: item.currentCode, accepted: true, name: item.name, specification: item.specification })}>确认适用</button><button className="text-button" disabled={feedback.isPending} onClick={() => feedback.mutate({ code: item.currentCode || item.rawCode, accepted: false, name: item.name, specification: item.specification })}>不适用</button></> : null}</div></article>)}</div>{search.data.items.length === 0 ? <div className="empty-guidance">没有匹配的正式实例，可到“联网补充”获取候选。</div> : null}</> : null}
  </div>;
}

function ExampleLibrary({ client, canOperate, canManage }: { client: ExportDocManagerApiClient; canOperate: boolean; canManage: boolean }) {
  const queryClient = useQueryClient();
  const [keyword, setKeyword] = useState(""); const [pageNumber, setPageNumber] = useState(1); const [pageSize, setPageSize] = useState(30);
  const [editing, setEditing] = useState<HsCodeKnowledgeExampleInput | null>(null); const [selected, setSelected] = useState<Set<number>>(new Set()); const [message, setMessage] = useState("");
  const data = useQuery({ queryKey: ["hs-knowledge", "examples", keyword, pageNumber, pageSize], queryFn: () => client.listHsCodeKnowledgeExamples({ keyword, pageNumber, pageSize }) });
  const invalidate = async () => { setSelected(new Set()); await queryClient.invalidateQueries({ queryKey: ["hs-knowledge", "examples"] }); };
  const save = useMutation({ mutationFn: (value: HsCodeKnowledgeExampleInput) => client.saveHsCodeKnowledgeExample({ body: value }), onSuccess: async () => { setEditing(null); setMessage("申报实例已保存。"); await invalidate(); }, onError: error => setMessage(readApiError(error)) });
  const remove = useMutation({ mutationFn: (id: number) => client.deleteHsCodeKnowledgeExample({ id }), onSuccess: async () => { setMessage("申报实例已删除。"); await invalidate(); }, onError: error => setMessage(readApiError(error)) });
  const batchRemove = useMutation({ mutationFn: (ids: number[]) => client.deleteHsCodeKnowledgeExamplesBatch({ body: { ids } }), onSuccess: async response => { setMessage(response.message); await invalidate(); }, onError: error => setMessage(readApiError(error)) });
  const blank: HsCodeKnowledgeExampleInput = { id: 0, rawReportedHsCode: "", resolvedCurrentHsCode: "", productName: "", specification: "", source: "Manual", sourceYear: new Date().getFullYear(), resolutionStatus: "Unresolved", isManuallyVerified: true };
  const totalPages = Math.max(1, Math.ceil((data.data?.totalCount ?? 0) / pageSize));
  useEffect(() => { setPageNumber(1); }, [keyword, pageSize]);
  function toggle(id: number) { setSelected(current => { const next = new Set(current); next.has(id) ? next.delete(id) : next.add(id); return next; }); }
  return <div className="knowledge-task-card"><div className="section-heading"><div><h2>申报实例库</h2><p>只保存已经人工确认、可以参与智能查询的公司经验。</p></div>{canOperate ? <button className="command-button" type="button" onClick={() => setEditing(blank)}>新增实例</button> : null}</div>
    <div className="knowledge-list-toolbar"><input className="knowledge-filter" value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="搜索名称、规格或HS编码"/>{canManage && selected.size ? <button className="secondary-button danger-button" type="button" onClick={() => window.confirm(`确定删除选中的 ${selected.size} 条实例吗？`) && batchRemove.mutate([...selected])}>批量删除（{selected.size}）</button> : null}</div>
    {message ? <div className={message.includes("失败") || message.includes("必须") ? "error-alert" : "success-alert"}>{message}</div> : null}
    {editing ? <form className="knowledge-editor" onSubmit={event => { event.preventDefault(); save.mutate(editing); }}><label>商品名称<input required value={editing.productName} onChange={event => setEditing({...editing, productName:event.target.value})}/></label><label>历史/原始编码<input required value={editing.rawReportedHsCode} onChange={event => setEditing({...editing, rawReportedHsCode:event.target.value})}/></label><label>当前有效编码<input value={editing.resolvedCurrentHsCode} onChange={event => setEditing({...editing, resolvedCurrentHsCode:event.target.value})}/></label><label>来源年份<input type="number" value={editing.sourceYear ?? ""} onChange={event => setEditing({...editing, sourceYear:event.target.value ? Number(event.target.value) : undefined})}/></label><label className="wide">规格与申报要素<textarea value={editing.specification} onChange={event => setEditing({...editing, specification:event.target.value})}/></label><div className="wide form-actions"><button className="command-button" disabled={save.isPending}>保存实例</button><button className="secondary-button" type="button" onClick={() => setEditing(null)}>取消</button></div></form> : null}
    {data.isLoading ? <div className="empty-guidance">正在读取申报实例...</div> : null}{data.isError ? <div className="error-alert">{readApiError(data.error)}</div> : null}
    <div className="knowledge-table">{data.data?.items.map(item => <article key={item.id}>{canManage ? <input type="checkbox" aria-label={`选择 ${item.productName}`} checked={selected.has(item.id)} onChange={() => toggle(item.id)}/> : null}<div><strong>{item.productName}</strong><span>{item.rawReportedHsCode}{item.resolvedCurrentHsCode && item.resolvedCurrentHsCode !== item.rawReportedHsCode ? ` → ${item.resolvedCurrentHsCode}` : ""}</span><p>{item.specification || "暂无规格说明"}</p><small>{item.source}{item.sourceYear ? ` · ${item.sourceYear}年` : ""}</small></div><div><span className="status-pill">{knowledgeStatusLabel(item.resolutionStatus)}</span>{canOperate ? <button type="button" onClick={() => setEditing({...blank, ...item, resolvedCurrentHsCode:item.resolvedCurrentHsCode ?? "", specification:item.specification ?? ""})}>编辑</button> : null}{canManage ? <button className="text-button danger" type="button" onClick={() => window.confirm("确定删除这条申报实例吗？") && remove.mutate(item.id)}>删除</button> : null}</div></article>)}</div>
    {data.data?.totalCount === 0 ? <div className="empty-guidance">暂无正式申报实例。</div> : null}
    <ListPaginationControls pageNumber={pageNumber} totalPages={totalPages} totalCount={data.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={data.isFetching} onPageChange={setPageNumber} onPageSizeChange={setPageSize}/>
  </div>;
}

function RemoteCandidateReview({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate: boolean }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState("Pending"); const [keyword, setKeyword] = useState(""); const [pageNumber, setPageNumber] = useState(1); const [pageSize, setPageSize] = useState(30);
  const [selected, setSelected] = useState<Set<number>>(new Set()); const [drafts, setDrafts] = useState<Record<number,string>>({}); const [message, setMessage] = useState("");
  const candidates = useQuery({ queryKey: ["hs-remote-candidates", status, keyword, pageNumber, pageSize], queryFn: () => client.listHsCodeRemoteCandidates({ status, keyword, pageNumber, pageSize }) });
  useEffect(() => { setPageNumber(1); setSelected(new Set()); }, [status, keyword, pageSize]);
  useEffect(() => { setDrafts(current => { const next = {...current}; for (const item of candidates.data?.items ?? []) if (!(item.id in next)) next[item.id] = item.suggestedCurrentHsCode ?? ""; return next; }); }, [candidates.data]);
  const refresh = async (value: string) => { setMessage(value); setSelected(new Set()); await queryClient.invalidateQueries({ queryKey: ["hs-remote-candidates"] }); await queryClient.invalidateQueries({ queryKey: ["hs-knowledge", "examples"] }); };
  const review = useMutation({ mutationFn: (value: { id:number; code:string; confirmed:boolean }) => client.reviewHsCodeRemoteCandidate({ body: { id:value.id, currentCode:value.code, confirmed:value.confirmed } }), onSuccess: response => void refresh(response.message), onError: error => setMessage(readApiError(error)) });
  const batchReview = useMutation({ mutationFn: (items: {id:number;currentCode:string;confirmed:boolean}[]) => client.reviewHsCodeRemoteCandidatesBatch({ body: { items } }), onSuccess: response => void refresh(response.message), onError: error => setMessage(readApiError(error)) });
  const reset = useMutation({ mutationFn: (ids:number[]) => client.resetHsCodeRemoteCandidates({ body:{ ids } }), onSuccess: response => void refresh(response.message), onError: error => setMessage(readApiError(error)) });
  const totalPages = Math.max(1, Math.ceil((candidates.data?.totalCount ?? 0) / pageSize));
  const selectedItems = (candidates.data?.items ?? []).filter(item => selected.has(item.id));
  function toggle(id:number) { setSelected(current => { const next=new Set(current); next.has(id)?next.delete(id):next.add(id); return next; }); }
  return <section className="remote-candidate-review"><div className="section-heading"><div><h2>联网候选审核</h2><p>待审核、已确认和已忽略记录分别保存；历史决定可以恢复为待审核。</p></div><button className="secondary-button" type="button" onClick={() => void candidates.refetch()}><RefreshCw size={16}/>刷新</button></div>
    <div className="candidate-status-tabs">{[["Pending","待审核"],["Confirmed","已确认"],["Ignored","已忽略"]].map(([key,label]) => <button type="button" className={status===key?"active":""} key={key} onClick={() => setStatus(key)}>{label}</button>)}</div>
    <div className="knowledge-list-toolbar"><input className="knowledge-filter" value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="筛选品名、规格、查询词或编码"/><div className="candidate-batch-actions">{canOperate && status === "Pending" && selectedItems.length ? <><button className="secondary-button" type="button" onClick={() => batchReview.mutate(selectedItems.filter(item => drafts[item.id]).map(item => ({id:item.id,currentCode:drafts[item.id],confirmed:true})))}>批量确认可用编码</button><button className="secondary-button" type="button" onClick={() => batchReview.mutate(selectedItems.map(item => ({id:item.id,currentCode:"",confirmed:false})))}>批量忽略</button></> : null}{canOperate && status !== "Pending" && selectedItems.length ? <button className="secondary-button" type="button" onClick={() => reset.mutate(selectedItems.map(item => item.id))}>恢复待审核（{selectedItems.length}）</button> : null}</div></div>
    {message ? <div className="success-alert">{message}</div> : null}{candidates.isLoading ? <div className="empty-guidance">正在读取联网候选...</div> : null}{candidates.isError ? <div className="error-alert">{readApiError(candidates.error)}</div> : null}
    <div className="knowledge-table">{candidates.data?.items.map(item => <article key={item.id}>{canOperate ? <input type="checkbox" aria-label={`选择 ${item.productName}`} checked={selected.has(item.id)} onChange={() => toggle(item.id)}/> : null}<div><strong>{item.productName}</strong><span>{item.rawReportedHsCode}{item.suggestedCurrentHsCode && item.suggestedCurrentHsCode !== item.rawReportedHsCode ? ` → ${item.suggestedCurrentHsCode}` : ""}</span><p>{item.specification || "暂无规格"}</p><small>查询词：{item.queryText} · 出现 {item.seenCount} 次 · {item.source}</small>{item.reviewedAt ? <small>审核时间：{formatVerifiedAt(item.reviewedAt).replace("验证于 ", "")}</small> : null}{item.sourceUrl ? <a className="knowledge-source-link" href={item.sourceUrl} target="_blank" rel="noreferrer">查看来源<ExternalLink size={13}/></a> : null}</div><div className="remote-candidate-actions"><span className="status-pill">{knowledgeStatusLabel(item.resolutionStatus)}</span>{status === "Pending" && canOperate ? <><RemoteCandidatePicker client={client} candidate={item} draft={drafts[item.id] ?? ""} disabled={review.isPending || batchReview.isPending} onDraft={value => setDrafts(current => ({...current,[item.id]:value}))} onConfirm={code => review.mutate({id:item.id,code,confirmed:true})}/><button className="text-button" type="button" disabled={review.isPending} onClick={() => review.mutate({id:item.id,code:"",confirmed:false})}>忽略</button></> : null}{status !== "Pending" && canOperate ? <button className="secondary-button" type="button" onClick={() => reset.mutate([item.id])}>恢复待审核</button> : null}</div></article>)}</div>
    {candidates.data?.totalCount === 0 ? <div className="empty-guidance"><strong>当前分类暂无记录</strong><span>联网查询获得的申报实例会进入待审核列表。</span></div> : null}
    <ListPaginationControls pageNumber={pageNumber} totalPages={totalPages} totalCount={candidates.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={candidates.isFetching} onPageChange={setPageNumber} onPageSizeChange={setPageSize}/>
  </section>;
}

function RemoteCandidatePicker({ client, candidate, draft, disabled, onDraft, onConfirm }: { client: ExportDocManagerApiClient; candidate: HsCodeRemoteCandidate; draft:string; disabled:boolean; onDraft:(value:string)=>void; onConfirm:(code:string)=>void }) {
  const lookup = useQuery({ queryKey:["hs-active-code-picker",candidate.id,draft.trim()], queryFn:()=>client.listHsCodes({pageNumber:1,pageSize:30,keyword:draft.trim()}), enabled:draft.trim().length>=4 });
  const activeItems=(lookup.data?.items??[]).filter(item=>item.status==="Active"&&item.sourceName&&item.effectiveYear&&item.lastVerifiedAt);
  const selected=activeItems.find(item=>(item.code||item.normalizedCode).replace(/[^0-9A-Za-z]/g,"")===draft.replace(/[^0-9A-Za-z]/g,""));
  return <div className="remote-candidate-picker"><input aria-label="选择当前有效HS编码" list={`active-hs-codes-${candidate.id}`} value={draft} onChange={event=>onDraft(event.target.value)} placeholder="输入有效编码检索"/><datalist id={`active-hs-codes-${candidate.id}`}>{activeItems.map(item=><option key={item.normalizedCode} value={item.code||item.normalizedCode}>{item.name} · {item.effectiveYear}年</option>)}</datalist><button className="command-button" type="button" disabled={disabled||!selected} onClick={()=>selected&&onConfirm(selected.code||selected.normalizedCode)}>{lookup.isFetching?"检索中":selected?"确认加入":"选择有效编码"}</button></div>;
}

function HistoryLearning({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate:boolean }) {
  const queryClient=useQueryClient(); const [keyword,setKeyword]=useState(""); const [message,setMessage]=useState("");
  const candidates=useQuery({queryKey:["hs-history-candidates",keyword],queryFn:()=>client.discoverHsCodeHistoryCandidates({keyword})});
  const confirm=useMutation({mutationFn:(item:NonNullable<typeof candidates.data>[number])=>client.saveHsCodeKnowledgeExample({body:{id:0,rawReportedHsCode:item.rawCode,resolvedCurrentHsCode:item.currentCode,productName:item.productName,specification:item.specification,source:`HistoryConfirmed:${item.source}`,sourceYear:new Date().getFullYear(),resolutionStatus:"ManuallyVerified",isManuallyVerified:true}}),onSuccess:async()=>{setMessage("历史经验已确认并加入申报实例库。");await queryClient.invalidateQueries({queryKey:["hs-history-candidates"]});await queryClient.invalidateQueries({queryKey:["hs-knowledge","examples"]});},onError:error=>setMessage(readApiError(error))});
  return <div className="knowledge-task-card"><div className="section-heading"><div><h2>从历史资料提取待确认经验</h2><p>扫描商品资料、历史发票和报关资料；确认后才进入正式实例库。</p></div></div><input className="knowledge-filter" value={keyword} onChange={event=>setKeyword(event.target.value)} placeholder="筛选商品名称、材质、品牌或HS编码"/>{message?<div className="success-alert">{message}</div>:null}{candidates.isLoading?<div className="empty-guidance">正在分析历史资料...</div>:null}{candidates.isError?<div className="error-alert">{readApiError(candidates.error)}</div>:null}<div className="knowledge-table">{candidates.data?.map(item=><article key={item.fingerprint}><div><strong>{item.productName}</strong><span>{item.rawCode}{item.currentCode&&item.currentCode!==item.rawCode?` → ${item.currentCode}`:""}</span><p>{item.specification||"暂无规格"}</p><small>{item.source} · 历史出现 {item.sourceCount} 次</small></div><div><span className="status-pill">{knowledgeStatusLabel(item.resolutionStatus)}</span>{canOperate?<button className="command-button" type="button" disabled={!item.canConfirm||confirm.isPending} onClick={()=>confirm.mutate(item)}>{item.canConfirm?"确认加入":"需先处理编码"}</button>:null}</div></article>)}</div>{candidates.data?.length===0?<div className="empty-guidance">没有新的待确认资料。</div>:null}</div>;
}

function KnowledgeTransfer({client,canOperate}:{client:ExportDocManagerApiClient;canOperate:boolean}) { const input=useRef<HTMLInputElement>(null);const[message,setMessage]=useState("");const importer=useMutation({mutationFn:(file:File)=>client.importHsCodeKnowledge({body:file}),onSuccess:value=>setMessage(value.result.message),onError:error=>setMessage(readApiError(error))});async function download(){const blob=await client.exportHsCodeKnowledge();const url=URL.createObjectURL(blob);const anchor=document.createElement("a");anchor.href=url;anchor.download=`ExportDocManager-HsLibrary-${new Date().toISOString().slice(0,10).replace(/-/g,"")}.edmhs`;anchor.click();URL.revokeObjectURL(url);}return <div className="knowledge-task-grid"><article className="knowledge-task-card"><Download size={28}/><h2>导出本机知识库</h2><p>包含已验证税则、正式申报实例、替代关系和学习记录，不包含业务单据和账号数据。</p><button className="command-button" type="button" onClick={()=>void download()}>导出 .edmhs</button></article><article className="knowledge-task-card"><Upload size={28}/><h2>导入另一台电脑</h2><p>导入前校验完整性；缺少有效编码来源、年度或验证时间的知识包会被拒绝。</p><input ref={input} hidden type="file" accept=".edmhs" onChange={event=>{const file=event.target.files?.[0];if(file)importer.mutate(file);}}/>{canOperate?<button className="command-button" type="button" disabled={importer.isPending} onClick={()=>input.current?.click()}>选择知识库文件</button>:null}</article>{message?<div className="alert knowledge-transfer-message">{message}</div>:null}</div>; }

function knowledgeStatusLabel(status?:string){const labels:Record<string,string>={Active:"当前有效",SuggestedReplacement:"待核验替代",WebRecommended:"网页推荐待核验",ObsoleteMapped:"已确认替代",ObsoleteUnresolved:"未找到替代",Ambiguous:"多条替代待选",ManuallyVerified:"人工已确认",Unresolved:"待处理"};return labels[status??""]??status??"待处理";}
function formatVerifiedAt(value?:string){if(!value)return"未标明验证时间";const date=new Date(value);return Number.isNaN(date.getTime())?value:`验证于 ${date.toLocaleDateString("zh-CN")}`;}
