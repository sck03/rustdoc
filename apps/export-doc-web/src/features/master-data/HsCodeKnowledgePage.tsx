import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookOpen, CloudDownload, Database, Download, GraduationCap, Search, Sparkles, Upload } from "lucide-react";
import { type FormEvent, useRef, useState } from "react";
import { Link, Navigate, useParams } from "react-router-dom";
import type { ExportDocManagerApiClient, HsCodeKnowledgeExampleInput } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { HsCodeToolsPanel } from "./HsCodeToolsPanel.tsx";

const sections = [
  ["search", "智能查询", Search], ["examples", "申报实例库", BookOpen],
  ["history", "历史资料学习", GraduationCap],
  ["annual", "年度税则", Database], ["transfer", "换机迁移", Download], ["online", "联网补充", CloudDownload],
] as const;

export function HsCodeKnowledgePage({ client }: { client: ExportDocManagerApiClient }) {
  const { section = "search" } = useParams();
  if (!sections.some(([key]) => key === section)) return <Navigate to="/master-data/hs-knowledge/search" replace />;
  return <section className="work-surface hs-knowledge-surface">
    <header className="page-heading"><div><span className="eyebrow">HS KNOWLEDGE CENTER</span><h1>HS 编码知识中心</h1><p>先查询、再确认，系统会保留申报经验并自动识别过期编码。</p></div><Link className="secondary-button" to="/master-data/hs-codes">返回标准编码库</Link></header>
    <nav className="hs-knowledge-nav" aria-label="HS知识中心功能">
      {sections.map(([key, label, Icon]) => <Link key={key} className={section === key ? "active" : ""} to={`/master-data/hs-knowledge/${key}`}><Icon size={18}/><span>{label}</span></Link>)}
    </nav>
    {section === "search" ? <KnowledgeSearch client={client}/> : null}
    {section === "examples" ? <ExampleLibrary client={client}/> : null}
    {section === "history" ? <HistoryLearning client={client}/> : null}
    {section === "transfer" ? <KnowledgeTransfer client={client}/> : null}
    {section === "annual" || section === "online" ? <div className="knowledge-task-card"><h2>{section === "annual" ? "年度税则资料" : "联网补充申报实例"}</h2><p>{section === "annual" ? "导入新年度完整税则前先预检差异，过期编码不会直接作为可用编码推荐。" : "联网结果会保存为申报实例；后续优先使用本地知识库，网页改版也不会丢失已积累经验。"}</p><HsCodeToolsPanel client={client} disabled={false} keyword="" onLocalDataChanged={async () => undefined}/></div> : null}
  </section>;
}

function HistoryLearning({ client }: { client: ExportDocManagerApiClient }) {
  const queryClient = useQueryClient(); const [keyword, setKeyword] = useState("");
  const candidates = useQuery({ queryKey: ["hs-history-candidates", keyword], queryFn: () => client.discoverHsCodeHistoryCandidates(keyword) });
  const confirm = useMutation({ mutationFn: (item: NonNullable<typeof candidates.data>[number]) => client.saveHsCodeKnowledgeExample({ id: 0, rawReportedHsCode: item.rawCode, resolvedCurrentHsCode: item.currentCode, productName: item.productName, specification: item.specification, source: `HistoryConfirmed:${item.source}`, sourceYear: new Date().getFullYear(), resolutionStatus: "ManuallyVerified", isManuallyVerified: true }), onSuccess: () => { void queryClient.invalidateQueries({ queryKey: ["hs-history-candidates"] }); void queryClient.invalidateQueries({ queryKey: ["hs-knowledge-examples"] }); } });
  return <div className="knowledge-task-card"><div className="section-heading"><div><h2>从自己的历史资料学习</h2><p>这里只生成待审核候选。只有点击“确认加入”的记录才会成为正式申报实例，系统猜测不会自动学习。</p></div></div><input className="knowledge-filter" value={keyword} onChange={e => setKeyword(e.target.value)} placeholder="筛选商品名称、材质、品牌或HS编码"/>
    <div className="knowledge-table">{candidates.data?.map(item => <article key={item.fingerprint}><div><strong>{item.productName}</strong><span>{item.rawCode}{item.currentCode && item.currentCode !== item.rawCode ? ` → ${item.currentCode}` : ""}</span><p>{item.specification || "暂无规格"}</p><small>{item.source} · 历史出现 {item.sourceCount} 次</small></div><div><span className="status-pill">{item.resolutionStatus}</span><button className="command-button" disabled={!item.canConfirm || confirm.isPending} onClick={() => confirm.mutate(item)}>{item.canConfirm ? "确认加入" : "需先处理编码"}</button></div></article>)}</div>{candidates.data?.length === 0 ? <div className="empty-guidance"><strong>没有新的待确认资料</strong><span>已确认或没有填写HS编码的历史商品不会重复显示。</span></div> : null}</div>;
}

function KnowledgeSearch({ client }: { client: ExportDocManagerApiClient }) {
  const [draft, setDraft] = useState(""); const [query, setQuery] = useState("");
  const search = useQuery({ queryKey: ["hs-knowledge-search", query], queryFn: () => client.searchHsCodeKnowledge(query), enabled: !!query });
  const feedback = useMutation({ mutationFn: (value: { code: string; accepted: boolean; name: string; specification: string }) => client.recordHsCodeKnowledgeFeedback({ queryText: query, productName: value.name, specification: value.specification, candidateCode: value.code, accepted: value.accepted }), onSuccess: () => void search.refetch() });
  function submit(event: FormEvent) { event.preventDefault(); if (draft.trim()) setQuery(draft.trim()); }
  return <div className="knowledge-task-card knowledge-search-card"><div className="knowledge-intro"><Sparkles size={24}/><div><h2>像普通商品名称一样查询</h2><p>例如“棉制针织男式T恤衫”，无需先知道税则中的标准名称。</p></div></div><form className="knowledge-search-form" onSubmit={submit}><input value={draft} onChange={e => setDraft(e.target.value)} placeholder="输入商品名称、材质、用途或规格"/><button className="command-button" type="submit">查询本地知识库</button></form>
    {search.isError ? <div className="error-alert">{readApiError(search.error)}</div> : null}{search.data ? <><p className="muted-text">{search.data.message}</p><div className="knowledge-result-list">{search.data.items.map(item => <article key={`${item.currentCode}-${item.rawCode}`} className={`knowledge-result ${item.canUse ? "usable" : "warning"}`}><div><span className="status-pill">{item.resolutionStatus}</span><h3>{item.name || item.standardName}</h3><strong>{item.currentCode || item.rawCode}</strong>{item.currentCode && item.rawCode !== item.currentCode ? <small>历史编码 {item.rawCode}</small> : null}<p>{item.specification || item.standardName}</p></div><div className="knowledge-score"><b>{item.score}</b><span>匹配度</span><small>{item.exampleCount} 条实例</small><button disabled={!item.canUse || feedback.isPending} onClick={() => feedback.mutate({ code: item.currentCode, accepted: true, name: item.name, specification: item.specification })}>确认适用</button><button className="text-button" disabled={feedback.isPending} onClick={() => feedback.mutate({ code: item.currentCode || item.rawCode, accepted: false, name: item.name, specification: item.specification })}>不适用</button></div></article>)}</div></> : null}
  </div>;
}

function ExampleLibrary({ client }: { client: ExportDocManagerApiClient }) {
  const queryClient = useQueryClient(); const [keyword, setKeyword] = useState(""); const [editing, setEditing] = useState<HsCodeKnowledgeExampleInput | null>(null);
  const data = useQuery({ queryKey: ["hs-knowledge-examples", keyword], queryFn: () => client.listHsCodeKnowledgeExamples(keyword) });
  const save = useMutation({ mutationFn: (value: HsCodeKnowledgeExampleInput) => client.saveHsCodeKnowledgeExample(value), onSuccess: () => { setEditing(null); void queryClient.invalidateQueries({ queryKey: ["hs-knowledge-examples"] }); } });
  const remove = useMutation({ mutationFn: (id: number) => client.deleteHsCodeKnowledgeExample(id), onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["hs-knowledge-examples"] }) });
  const blank: HsCodeKnowledgeExampleInput = { id: 0, rawReportedHsCode: "", resolvedCurrentHsCode: "", productName: "", specification: "", source: "Manual", sourceYear: new Date().getFullYear(), resolutionStatus: "Unresolved", isManuallyVerified: true };
  return <div className="knowledge-task-card"><div className="section-heading"><div><h2>申报实例库</h2><p>可搜索、修正和确认实际申报经验，不会改动商业发票数据。</p></div><button className="command-button" onClick={() => setEditing(blank)}>新增实例</button></div><input className="knowledge-filter" value={keyword} onChange={e => setKeyword(e.target.value)} placeholder="搜索名称、规格或HS编码"/>
    {editing ? <form className="knowledge-editor" onSubmit={e => { e.preventDefault(); save.mutate(editing); }}><label>商品名称<input required value={editing.productName} onChange={e => setEditing({...editing, productName:e.target.value})}/></label><label>历史/原始编码<input required value={editing.rawReportedHsCode} onChange={e => setEditing({...editing, rawReportedHsCode:e.target.value})}/></label><label>当前有效编码<input value={editing.resolvedCurrentHsCode} onChange={e => setEditing({...editing, resolvedCurrentHsCode:e.target.value})}/></label><label className="wide">规格与申报要素<textarea value={editing.specification} onChange={e => setEditing({...editing, specification:e.target.value})}/></label><div className="wide form-actions"><button className="command-button" disabled={save.isPending}>保存实例</button><button className="secondary-button" type="button" onClick={() => setEditing(null)}>取消</button></div></form> : null}
    <div className="knowledge-table">{data.data?.items.map(item => <article key={item.id}><div><strong>{item.productName}</strong><span>{item.rawReportedHsCode}{item.resolvedCurrentHsCode && item.resolvedCurrentHsCode !== item.rawReportedHsCode ? ` → ${item.resolvedCurrentHsCode}` : ""}</span><p>{item.specification || "暂无规格说明"}</p></div><div><span className="status-pill">{item.resolutionStatus}</span><button onClick={() => setEditing({...blank, ...item, resolvedCurrentHsCode:item.resolvedCurrentHsCode ?? "", specification:item.specification ?? ""})}>编辑</button><button className="text-button danger" onClick={() => remove.mutate(item.id)}>删除</button></div></article>)}</div></div>;
}

function KnowledgeTransfer({ client }: { client: ExportDocManagerApiClient }) {
  const input = useRef<HTMLInputElement>(null); const [message, setMessage] = useState("");
  const importer = useMutation({ mutationFn: (file: File) => client.importHsCodeKnowledge(file), onSuccess: value => setMessage(value.result.message), onError: error => setMessage(readApiError(error)) });
  async function download() { const blob = await client.exportHsCodeKnowledge(); const url = URL.createObjectURL(blob); const anchor = document.createElement("a"); anchor.href=url; anchor.download=`ExportDocManager-HsLibrary-${new Date().toISOString().slice(0,10).replace(/-/g,"")}.edmhs`; anchor.click(); URL.revokeObjectURL(url); }
  return <div className="knowledge-task-grid"><article className="knowledge-task-card"><Download size={28}/><h2>导出本机知识库</h2><p>包含HS主数据、申报实例、替代关系和学习记录，不包含发票、客户、付款或账号数据。</p><button className="command-button" onClick={() => void download()}>导出 .edmhs</button></article><article className="knowledge-task-card"><Upload size={28}/><h2>导入另一台电脑</h2><p>自动校验完整性并合并去重，已有人工确认不会被较旧记录覆盖。</p><input ref={input} hidden type="file" accept=".edmhs" onChange={e => { const file=e.target.files?.[0]; if(file) importer.mutate(file); }}/><button className="command-button" disabled={importer.isPending} onClick={() => input.current?.click()}>选择知识库文件</button></article>{message ? <div className="alert knowledge-transfer-message">{message}</div> : null}</div>;
}
