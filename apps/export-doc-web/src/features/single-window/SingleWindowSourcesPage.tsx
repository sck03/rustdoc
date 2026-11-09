import { Link } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { RecordWorkspace } from "../../ui/layout/Workspaces.tsx";
import { TextField } from "../../ui/FormFields.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { useSingleWindowSources } from "./useSingleWindowSources.ts";
import "../../styles/routes/single-window.css";

export function SingleWindowSourcesPage({ client }: { client: ExportDocManagerApiClient }) {
  const work = useSingleWindowSources(client);
  if (!work.permission.canView) return <PermissionNotice>没有申报资料查看权限。</PermissionNotice>;
  const imported = work.preview.preview;
  return <RecordWorkspace title="申报资料" className="single-window-surface">
    <SingleWindowTabs activeKey="sources" />
    {work.message && <InlineNotice tone="error">{work.message}</InlineNotice>}
    {work.success && <InlineNotice tone="success">{work.success}</InlineNotice>}
    {work.permission.canOperate && <form className="form-section" onSubmit={event => { event.preventDefault(); void work.create(); }}>
      <div className="section-header"><h2>新建申报资料</h2></div>
      <div className="field-grid">
        <TextField label="资料编号 / 客户发票号" value={work.referenceNo} onChange={work.setReferenceNo} required disabled={work.busy} />
        <label className="form-field"><span className="form-field-label">客户 Excel（可选）</span><input type="file" disabled={work.busy} accept=".xlsx,.xlsm,.xltx,.xltm,.xls"
          onChange={event => { work.select(event.currentTarget.files?.[0]); event.currentTarget.value = ""; }} /></label>
      </div>
        {work.fileName && <div className="toolbar-actions"><span>{work.fileName}</span><button className="command-button secondary" type="button" disabled={work.busy} onClick={work.clearFile}>清除文件，手工录入</button></div>}
        {imported && <div>
          <p>{imported.success ? `已识别 ${imported.invoice?.items.length ?? 0} 行商品，请核对后创建。` : "未识别出有效资料。"}</p>
          {imported.errors.map((error, index) => <p key={index}>{error}</p>)}
          {imported.invoice && <div className="table-scroll"><table><thead><tr><th>中文品名</th><th>英文品名</th><th>HS 编码</th><th>数量</th></tr></thead>
            <tbody>{imported.invoice.items.slice(0, 20).map((item, index) => <tr key={index}><td>{item.styleNameCN}</td><td>{item.styleName}</td><td>{item.hsCode}</td><td>{item.quantity}</td></tr>)}</tbody></table></div>}
        </div>}
      <div className="form-actions"><button className="command-button" type="submit" disabled={work.busy || !work.referenceNo.trim() || Boolean(work.fileName && !imported?.success)}>创建申报资料</button></div>
      <p>不选择 Excel 可直接手工填写。产地证收货人、品名及明细单独保存。</p>
    </form>}
    <section className="form-section">
      <TextField label="查找资料" value={work.keyword} onChange={value => { work.setKeyword(value); work.setPage(1); }} />
      {work.query.isError && <InlineNotice tone="error">{readApiError(work.query.error)}</InlineNotice>}
      <div className="table-scroll"><table><thead><tr><th>资料编号</th><th>来源</th><th>填写申报</th></tr></thead>
        <tbody>{(work.query.data?.items ?? []).map(row => <tr key={row.id}>
          <td>{row.invoiceNo}</td><td>{row.sourceKind === "Excel" ? "客户 Excel" : "手工录入"}</td>
          <td><div className="toolbar-actions"><Link className="command-button secondary" to={`/single-window/acd/${row.id}`}>代理委托</Link><Link className="command-button secondary" to={`/single-window/coo/${row.id}`}>原产地证</Link></div></td>
        </tr>)}</tbody></table></div>
      <Pagination pageNumber={work.page} totalCount={work.query.data?.totalCount ?? 0} pageSize={50}
        isBusy={work.query.isFetching} onPageChange={work.setPage} />
    </section>
  </RecordWorkspace>;
}
