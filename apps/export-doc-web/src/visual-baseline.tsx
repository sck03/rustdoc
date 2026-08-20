import React from "react";
import ReactDOM from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BellRing, Building2, ContactRound, Download, FolderOpen, Landmark, RefreshCw, Search } from "lucide-react";
import { HashRouter } from "react-router-dom";
import { WorkspaceShell } from "./app/WorkspaceShell.tsx";
import { LoginPage } from "./features/auth/LoginPage.tsx";
import { ConfirmationDialog } from "./ui/ConfirmationDialog.tsx";
import { FrontendFatalErrorState } from "./ui/FrontendErrorBoundary.tsx";
import { ConcurrencyConflictNotice, FormGuidance, InlineNotice, PageState, PermissionNotice } from "./ui/PageState.tsx";
import { getProductEditionPresentation } from "./app/productEdition.ts";
import { applyInterfaceDensity, persistInterfaceDensity, readInterfaceDensity } from "./app/interfaceDensity.ts";
import { getWorkspaceDeviceCapabilities, useWorkspaceDeviceMode } from "./app/workspaceDevice.ts";
import { WorkspaceDeviceNotice } from "./ui/WorkspaceDeviceNotice.tsx";
import "./styles/visual-baseline.css";

const visualSearch = new URLSearchParams(location.search);
const page = visualSearch.get("page") ?? "dashboard";
const fullProduct = getProductEditionPresentation("Full");
const visualQueryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: false },
    mutations: { retry: false },
  },
});
const requestedDensity = visualSearch.get("density");
if (requestedDensity === "compact" || requestedDensity === "comfortable") {
  persistInterfaceDensity(requestedDensity);
} else {
  applyInterfaceDensity(readInterfaceDensity());
}
const pathnameByPage: Record<string, string> = {
  dashboard: "/dashboard",
  query: "/query/invoices",
  invoice: "/invoices/new",
  invoiceParties: "/invoices/new",
  hs: "/master-data/hs-knowledge/online",
  singleWindow: "/single-window/coo/1",
  report: "/reports/templates",
};

function BaselineApp() {
  if (page === "login" || page === "login-expired") {
    return <LoginPage apiBaseUrl="http://127.0.0.1:5188" username="admin" password="" bootstrapToken="" isDesktopRuntime={false} isBusy={false}
      message={page === "login-expired" ? "登录状态已失效，请重新登录后继续。为保护账号安全，系统没有重复提交刚才的操作。" : null}
      product={fullProduct}
      onApiBaseUrlChange={() => undefined} onUsernameChange={() => undefined} onPasswordChange={() => undefined}
      onBootstrapTokenChange={() => undefined} onSubmit={(event) => event.preventDefault()} />;
  }
  if (page === "state-fatal") {
    return <FrontendFatalErrorState incidentId="WEB-20260722-TEST1" onRetry={() => undefined} onReload={() => undefined} />;
  }
  const user = {
    username: "admin", fullName: "系统管理员", role: "Admin",
    capabilities: { canManageSettings: true, canUseDocumentWorkspace: true, canUseSalesWorkspace: true, productEdition: "Full", enabledModules: undefined },
  } as never;
  return <HashRouter><WorkspaceShell pathname={pathnameByPage[page] ?? "/dashboard"} apiBaseUrl="http://127.0.0.1:5188" isDesktopRuntime={page === "state-offline-local"} user={user} onLogout={() => undefined} connectivityOverride={page === "state-offline" || page === "state-offline-local" ? "offline" : "online"} serviceAvailabilityOverride={page === "state-service-unavailable" ? "unreachable" : "available"} notice={page === "state-route-redirect" ? { id: "permission", tone: "warning", title: "当前页面不可用", message: "当前权限模板未启用报表设计，系统已返回当前账号可以使用的工作区。" } : null} onDismissNotice={() => undefined}>
    {page === "query" ? <QueryBaseline/> : page === "invoice" ? <InvoiceBaseline/> : page === "invoiceParties" ? <InvoicePartiesBaseline/> : page === "hs" ? <HsBaseline/> : page === "singleWindow" ? <SingleWindowBaseline/> : page === "report" ? <ReportBaseline/> : page === "state-loading" ? <StateBaseline tone="loading"/> : page === "state-empty" ? <StateBaseline tone="empty"/> : page === "state-error" ? <StateBaseline tone="error"/> : page === "state-permission" ? <StateBaseline tone="permission"/> : page === "state-conflict" ? <ConflictBaseline/> : page === "state-feedback" ? <FeedbackBaseline/> : page === "dialog" ? <DialogBaseline/> : <DashboardBaseline/>}
  </WorkspaceShell></HashRouter>;
}

function StateBaseline({ tone }: { tone: "loading" | "empty" | "error" | "permission" }) {
  const content = tone === "loading" ? ["正在加载发票资料", "系统正在读取客户、商品明细和单据设置。"]
    : tone === "empty" ? ["暂无申报实例", "调整查询条件，或通过联网补充获取待审核候选。"]
      : tone === "error" ? ["数据加载失败", "无法连接业务服务，请检查网络后重试。"]
        : ["当前账号无操作权限", "您可以查看数据，但不能保存、审核或删除记录。"];
  return <section className="work-surface"><div className="section-header"><div><h2>统一页面状态</h2><p>加载、空数据、失败和权限状态使用一致的视觉与辅助语义。</p></div></div><PageState tone={tone} title={content[0]} description={content[1]} action={tone === "error" ? <button type="button" className="command-button">重新加载</button> : undefined}/>{tone === "permission" ? <PermissionNotice>如需操作权限，请联系系统管理员调整当前岗位模板。</PermissionNotice> : null}{tone === "empty" ? <FormGuidance title="先建立产品资料" description="供货关系必须关联现有产品，完成后即可返回继续。" action={<button type="button" className="secondary-button">打开产品资料</button>} /> : null}</section>;
}

function ConflictBaseline() {
  return <section className="work-surface"><div className="section-header"><div><h2>多人协同保护</h2><p>检测到服务器数据更新时，不覆盖其他用户刚保存的内容。</p></div></div><ConcurrencyConflictNotice message="该发票数据已被其他用户修改，请刷新后重试。" onReload={() => undefined}/></section>;
}

function FeedbackBaseline() {
  return <section className="work-surface"><div className="section-header"><div><h2>操作反馈</h2><p>保存、导入和请求失败使用一致的图标、层级与辅助语义。</p></div></div><InlineNotice tone="success" title="保存成功">发票 YH2026-024 已保存，商品明细和汇总金额已经更新。</InlineNotice><InlineNotice tone="warning" title="部分参考资料未加载">单位资料暂时不可用，仍可继续编辑并稍后刷新。</InlineNotice><InlineNotice tone="error" title="操作未完成">无法连接业务服务，请检查网络后重试。</InlineNotice></section>;
}

function DialogBaseline() {
  return <><InvoiceBaseline/><ConfirmationDialog title="确认删除发票？" description="删除后该发票及其商品明细将无法恢复。" details={["发票号：YH2026-024", "建议先导出或确认该记录不再需要。"]} confirmLabel="删除发票" onCancel={() => undefined} onConfirm={() => undefined}/></>;
}

function DashboardBaseline() { return <section className="dashboard-page"><div className="dashboard-metric-grid">{["本月出口额","本月预估利润","本月退税额","待处理订单","已出运","总订单量"].map((label,index)=><div className="dashboard-metric dashboard-metric-teal" key={label}><div className="dashboard-metric-icon" aria-hidden="true">{index + 1}</div><div><span>{label}</span><strong data-visual-critical-text>{index < 3 ? "128,560.00" : String(12 + index)}</strong></div></div>)}</div><div className="dashboard-work-grid"><section className="form-section dashboard-recent-section"><div className="section-header"><h2>最新订单</h2></div><div className="table-frame" tabIndex={0} aria-label="最新订单表格"><table><thead><tr><th>发票号</th><th>状态</th><th>客户</th><th>日期</th><th>金额</th></tr></thead><tbody>{[1,2,3,4].map(i=><tr key={i}><td>YH2026-00{i}</td><td><span className="status-pill">处理中</span></td><td>BRIDGE GLOBAL LTD.</td><td>2026-07-{20+i}</td><td>32,140.00</td></tr>)}</tbody></table></div></section><section className="form-section dashboard-todo-section"><div className="section-header"><h2>待办事项</h2></div><div className="dashboard-todo-list">{["核对发票商品明细","审核联网 HS 候选","生成报关单证"].map(x=><button type="button" className="dashboard-todo-item" key={x}>{x}</button>)}</div></section></div></section>; }
function QueryBaseline() {
  const isDesktop = useWorkspaceDeviceMode() === "desktop";
  return (
    <section className="work-surface query-surface" aria-label="单据查询视觉基准">
      <form className="toolbar query-toolbar">
        <div className="query-filter-stack">
          <span className="query-filter-section-label">常用条件</span>
          <div className="query-filter-grid query-common-filter-grid">
            <div className="query-date-range" role="group" aria-label="日期范围">
              <label className="query-filter-field query-date-filter"><span>开始日期</span><input type="date" defaultValue="2026-08-01" /></label>
              <label className="query-filter-field query-date-filter"><span>结束日期</span><input type="date" defaultValue="2026-08-10" /></label>
            </div>
            <label className="query-filter-field query-keyword-filter"><span>关键字</span><input placeholder="发票号、合同号、客户等" /></label>
          </div>
          <details className="query-advanced-filters" open={isDesktop}>
            <summary className="query-advanced-filter-summary"><span>高级筛选</span><small>客户、出口商、类型与运输方式</small></summary>
            <div className="query-filter-grid query-advanced-filter-grid">
              <div className="remote-select-field query-filter-field query-party-filter query-customer-filter">
                <span className="form-field-label"><span>客户</span></span>
                <div className="remote-select-controls"><input type="search" aria-label="客户检索" placeholder="搜索客户" /><select aria-label="客户"><option>全部</option></select></div>
              </div>
              <div className="remote-select-field query-filter-field query-party-filter query-exporter-filter">
                <span className="form-field-label"><span>出口商</span></span>
                <div className="remote-select-controls"><input type="search" aria-label="出口商检索" placeholder="搜索出口商" /><select aria-label="出口商"><option>全部</option></select></div>
              </div>
              <label className="query-filter-field query-type-filter"><span>业务类型</span><select defaultValue=""><option value="">全部</option><option>实际数据</option></select></label>
              <label className="query-filter-field query-transport-filter"><span>运输方式</span><select defaultValue=""><option value="">全部</option><option>BY SEA</option></select></label>
            </div>
          </details>
        </div>
        <div className="toolbar-actions query-toolbar-actions">
          <button className="icon-button" type="button" title="刷新" aria-label="刷新"><RefreshCw size={18} aria-hidden="true" /></button>
          <button className="icon-button solid" type="submit" title="查询" aria-label="查询"><Search size={18} aria-hidden="true" /></button>
        </div>
      </form>
      <details className="form-section query-export-panel" aria-label="查询结果导出">
        <summary className="query-export-summary"><span>导出查询结果</span><small>0 条当前结果</small></summary>
        <div className="query-export-grid">
          <label className="inline-filter query-export-path-filter"><span>输出路径</span><input defaultValue="QueryResults.xlsx" /></label>
          <div className="query-inline-actions">
            <button className="icon-button" type="button" title="选择 Excel 保存位置" aria-label="选择 Excel 保存位置"><FolderOpen size={16} aria-hidden="true" /></button>
            <button className="icon-button solid" type="button" title="导出 Excel" aria-label="导出 Excel"><Download size={18} aria-hidden="true" /></button>
          </div>
        </div>
      </details>
      <div className="table-frame query-table-frame" tabIndex={0} aria-label="查询结果表格">
        <table className="query-table"><thead><tr>{["发票号","日期","合同号","客户","出口商","目的国","贸易条款","船期/航期","运输方式","箱数","数量","金额","类型"].map((column) => <th key={column}>{column}</th>)}</tr></thead><tbody><tr><td colSpan={13}><PageState tone="empty" title="暂无查询结果" description="调整筛选条件后执行查询。" /></td></tr></tbody></table>
      </div>
    </section>
  );
}
function InvoiceBaseline() {
  const columns = ["操作", "PO", "款号", "英文品名", "中文品名", "成分", "品牌", "HS 编码", "原产地", "数量", "单位 EN", "每箱", "箱数", "长", "宽", "高", "毛重/箱", "总毛重", "净重/箱", "总净重", "单价", "金额", "采购价", "退税率", "备注 1"];
  const values = ["复制/移动", "PO-2026-024", "TS-M001", "MEN'S COTTON KNITTED T-SHIRT", "男式棉制针织T恤衫", "100% COTTON", "BRIDGE", "6109100000", "CHINA", "1000", "PCS", "50", "20", "60", "40", "35", "12.50", "250.00", "11.80", "236.00", "4.50", "4,500.00", "3.10", "13", "S/S 2026"];
  return <section className="editor-surface"><div className="editor-toolbar"><div className="editor-title"><span>新建发票</span><span className="editor-save-state" data-state="dirty">有未保存修改</span></div></div><form className="invoice-form"><nav className="invoice-editor-section-nav"><button type="button" className="invoice-section-nav-item">发票表头</button><button type="button" className="invoice-section-nav-item invoice-section-nav-primary">商品明细</button><button type="button" className="invoice-section-nav-item">利润/信用证</button><button type="button" className="invoice-section-nav-item">预览导出</button></nav><div className="invoice-editor-sticky-actions"><div><strong>YH2026-024</strong><span>当前有未保存修改</span></div><button type="button" className="command-button">保存发票</button></div><section className="form-section"><div className="section-header"><h2>基本信息</h2></div><div className="form-grid">{["发票号","发票日期","客户","出口商","贸易条款","目的国"].map(x=><label key={x}>{x}<input value={x === "发票号" ? "YH2026-024" : ""} readOnly/></label>)}</div></section><section className="form-section"><div className="section-header"><div><h2>商品明细</h2><p className="section-description">视觉基准展示真实编辑表的主要字段；其余单位、体积和备用字段可通过列设置显示。</p></div></div><div className="table-frame item-editor-frame" tabIndex={0} aria-label="完整发票商品明细编辑表"><table className="item-editor-table" style={{minWidth: 3150}}><thead><tr>{columns.map(column=><th key={column}>{column}</th>)}</tr></thead><tbody><tr>{values.map((value,index)=><td key={columns[index]}>{index === 0 ? <button type="button" className="secondary-button">行操作</button> : <input aria-label={`第1行${columns[index]}`} value={value} readOnly/>}</td>)}</tr></tbody></table></div><div className="item-summary-bar"><span>1 行</span><span>数量 1,000</span><span>箱数 20</span><span>毛重 250.00</span><span>金额 USD 4,500.00</span></div></section></form></section>;
}
function InvoicePartiesBaseline() {
  const heading = (icon: React.ReactNode, title: string, description: string, status: string) => (
    <div className="invoice-party-group-heading">
      <span className="invoice-party-group-icon" aria-hidden="true">{icon}</span>
      <div><strong>{title}</strong><span>{description}</span></div>
      <small className="connected">{status}</small>
    </div>
  );
  return (
    <section className="editor-surface">
      <div className="editor-toolbar"><div className="editor-title">新建发票</div></div>
      <section className="form-section">
        <div className="section-header"><h2>客户与出口商</h2></div>
        <div className="invoice-party-groups">
          <section className="invoice-party-group">
            {heading(<ContactRound size={17} />, "客户信息", "关联档案后仍可调整本张发票内容", "已关联档案")}
            <div className="field-grid">
              <label>客户档案<select><option>测试客户档案</option></select></label>
              <label>客户英文名<input value="SOCIÉTÉ GÉNÉRALE INTERNATIONAL TRADING — SHANGHAI BRANCH (测试)" readOnly /></label>
              <label className="field-grid-span-all textarea-field invoice-party-address-field">客户英文地址<textarea value="ROOM 2801, INTERNATIONAL COMMERCE CENTER, 888 CENTURY AVENUE, PUDONG NEW AREA, SHANGHAI, CHINA № 200120" readOnly /></label>
            </div>
          </section>
          <section className="invoice-party-group">
            {heading(<BellRing size={17} />, "通知人信息", "仅在通知对象与客户不同时填写", "已填写")}
            <div className="field-grid">
              <label className="field-grid-span-all">通知人<input value="NOTIFY PARTY CO., LTD. — HONG KONG / 香港分公司" readOnly /></label>
              <label className="field-grid-span-all textarea-field invoice-party-address-field">通知人英文地址<textarea value="ROOM 1808, COMMERCIAL BUILDING, 128 QUEEN'S ROAD CENTRAL, HONG KONG SAR, CHINA (ATTN: IMPORT DEPT. ™)" readOnly /></label>
            </div>
          </section>
          <section className="invoice-party-group invoice-party-group-exporter">
            {heading(<Building2 size={17} />, "出口商与收款信息", "企业身份、海关、银行与印章集中维护", "已关联档案")}
            <div className="invoice-party-subsections">
              <section className="invoice-party-subsection">
                <h3><Building2 size={16} />企业与海关资料</h3>
                <div className="field-grid">
                  <label className="field-grid-span-2">出口商档案<select><option>宁波布利杰进出口有限公司</option></select></label>
                  <label className="field-grid-span-2">出口商英文名<input value="BRIDGE IMPORT & EXPORT CO., LTD." readOnly /></label>
                  <label className="field-grid-span-2">出口商中文名<input value="宁波布利杰进出口有限公司" readOnly /></label>
                  <label>统一信用代码<input value="91310000TEST2026X" readOnly /></label>
                  <label>出口商海关编码<input value="330296TEST" readOnly /></label>
                </div>
              </section>
              <section className="invoice-party-subsection">
                <h3><Landmark size={16} />收款与印章资料</h3>
                <div className="field-grid">
                  <label className="field-grid-span-2">银行名称<input value="BANK OF NINGBO" readOnly /></label>
                  <label className="field-grid-span-2">银行账号<input value="6222 0000 2026 001" readOnly /></label>
                  <label>SWIFT<input value="BKNBCN2N" readOnly /></label>
                  <label>汇率<input value="7.1000" readOnly /></label>
                </div>
              </section>
            </div>
          </section>
        </div>
      </section>
    </section>
  );
}
function HsBaseline() { return <section className="work-surface hs-knowledge-surface"><div className="knowledge-workflow">{["联网获取","匹配税则","人工审核","实例入库","智能使用"].map((x,i)=><div className={i===2?"active current":i<2?"active":""} key={x}><span>{i+1}</span><strong>{x}</strong><small>流程说明</small>{i===2?<em>当前步骤 3/5</em>:null}</div>)}</div><nav className="hs-knowledge-nav">{["智能查询","申报实例库","历史资料学习","年度税则","换机迁移","联网补充"].map(x=><a className={x==="联网补充"?"active":""} key={x}>{x}</a>)}</nav><div className="knowledge-task-card"><h2>联网候选审核</h2><p className="knowledge-task-lead">网页实例用于提供申报经验；必须匹配当前年度税则并由人工确认后才会入库。</p><div className="knowledge-table">{[1,2,3].map(i=><article className="remote-candidate-card" key={i}><div className="remote-candidate-evidence"><div className="remote-candidate-title"><strong>男式棉制针织T恤衫</strong><span className="status-pill">网页推荐待核验</span></div><div className="remote-code-comparison"><span><small>网页实例编码</small><b>61091000</b></span><i>→</i><span className="current-code"><small>待确认当前编码</small><b>6109100000</b></span></div><p>棉制、针织、男式、短袖</p><div className="remote-candidate-meta"><small>查询词：男T恤</small><small>网页出现 6 次</small><small>来源：i5a6</small></div></div><div className="remote-candidate-actions"><button type="button" className="command-button">确认加入当前编码</button><button type="button" className="text-button">忽略此实例</button></div></article>)}</div></div></section>; }
function SingleWindowBaseline() { return <section className="editor-surface"><div className="editor-toolbar single-window-document-toolbar"><div className="editor-title">海关原产地证</div><div className="single-window-command-band"><div className="single-window-view-mode"><button type="button" className="active">标准模式</button><button type="button">高级模式</button></div><div className="single-window-tool-group"><span className="single-window-tool-heading">草稿</span><button type="button" className="command-button secondary">回填空白</button></div><div className="single-window-tool-group"><button type="button" className="command-button secondary">预检</button></div><button type="button" className="command-button">保存草稿</button></div></div><section className="form-section"><div className="section-header"><h2>草稿状态</h2></div><div className="coo-completion-overview"><div className="coo-completion-heading"><div><span>录入完成度</span><strong>67%</strong></div><small>4/6 个关键项目已具备</small></div><div className="coo-completion-track"><span style={{width:"67%"}}/></div><div className="coo-completion-steps">{[["证书基础","已具备",true],["申报对象","已具备",true],["运输贸易","待补充",false],["商品明细","2 行",true],["附件","1 条",true],["预警","2 条",false]].map(([label,detail,complete])=><span className={complete?"complete":"pending"} key={String(label)}>{label}<b>{detail}</b></span>)}</div></div></section><section className="form-section"><div className="section-header"><h2>证书基本信息</h2></div><div className="form-grid">{["发票号","申请人","收货人","运输方式","签证机构","目的国"].map(x=><label key={x}>{x}<input readOnly/></label>)}</div></section></section>; }
function ReportBaseline() {
  const deviceMode = useWorkspaceDeviceMode();
  const isDesktop = getWorkspaceDeviceCapabilities(deviceMode).canUseDenseWorkbench;
  return <section className="work-surface report-template-surface"><div className="report-template-sticky-header"><div className="editor-toolbar"><div className="editor-title">报表设计</div><div className="toolbar-actions"><button type="button" className="command-button secondary">刷新</button><button type="button" className="command-button secondary">预览</button><button type="button" className="command-button" disabled={!isDesktop}>保存</button></div></div><div className="report-template-workspace-tabs"><button type="button" className={isDesktop ? "segmented-active" : ""} disabled={!isDesktop}>设计</button><button type="button" className={isDesktop ? "" : "segmented-active"}>预览</button></div></div><WorkspaceDeviceNotice mode={deviceMode} phone="可选择模板、查看预览和进行轻量确认；完整设计与模板包导入导出请使用桌面端。" tablet="可选择模板、查看预览和进行现场确认；完整设计与模板包导入导出请使用桌面端。"/><div className={isDesktop ? "report-template-grid report-template-grid-design report-template-grid-new" : "report-template-mobile-selection"}><aside className="report-template-sidebar"><div className="template-selection-panel"><label>类型<select><option>出口单证</option></select></label><label>默认模板<select><option>invoice_template</option></select></label><label>我的 / 共享模板<select><option>未选择</option></select></label></div>{isDesktop ? <><details className="template-management-panel template-actions-panel template-user-panel" open><summary><span>我的模板</span><small>默认私有，可明确共享</small></summary><div className="template-management-content"><button type="button" className="secondary-button">复制当前模板</button></div></details><details className="template-management-panel template-actions-panel template-admin-panel"><summary><span>模板操作</span><small>invoice_template.html</small></summary></details><details className="template-management-panel template-package-panel"><summary><span>模板包</span><small>导入 / 导出</small></summary></details></> : null}</aside>{isDesktop ? <main className="report-template-new-designer"><div className="form-section" style={{minHeight:420}}><h2>模板画布</h2><div className="table-frame"><table><tbody><tr><td>EXPORTER</td><td>INVOICE NO.</td></tr><tr><td>CONSIGNEE</td><td>DATE</td></tr></tbody></table></div></div></main> : <main className="form-section report-template-preview-workspace" style={{minHeight:360}}><div className="section-header"><div><h2>模板预览</h2><p>当前设备保持查看和确认模式。</p></div></div><div className="report-preview-empty">选择模板后在此查看分页和换行效果</div></main>}</div></section>;
}

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={visualQueryClient}>
      <BaselineApp/>
    </QueryClientProvider>
  </React.StrictMode>,
);
requestAnimationFrame(() => requestAnimationFrame(() => {
  document.querySelectorAll(".knowledge-workflow, .table-frame").forEach((element) => {
    if (!element.hasAttribute("tabindex")) element.setAttribute("tabindex", "0");
  });
  document.documentElement.dataset.visualBaselineReady = "true";
}));
