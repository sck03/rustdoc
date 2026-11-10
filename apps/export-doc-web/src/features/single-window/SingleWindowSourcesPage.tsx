import { FileInput } from "../../ui/FileInput.tsx";
import { Link } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { RecordWorkspace } from "../../ui/layout/Workspaces.tsx";
import { TextField } from "../../ui/FormFields.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { useSingleWindowSources } from "./useSingleWindowSources.ts";
import { ExcelImportModeSelect } from "../tools/excel/ExcelImportModeSelect.tsx";
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
      <div className="section-header"><h2>新建申报资料</h2><button type="button" className="command-button secondary" disabled={work.busy || !work.canDownloadTemplate} title={work.canDownloadTemplate ? undefined : "需要文件任务的查看和操作权限"} onClick={() => void work.downloadTemplate()}>下载 Excel 模板</button></div>
      <div className="field-grid">
        <TextField label="资料编号 / 客户发票号" value={work.referenceNo} onChange={work.setReferenceNo} required disabled={work.busy} />
        <ExcelImportModeSelect client={client} value={work.schemeName} onChange={work.changeScheme} disabled={work.busy} />
        <label className="form-field"><span className="form-field-label">客户 Excel（可选）</span><FileInput disabled={work.busy} accept=".xlsx,.xlsm,.xltx,.xltm,.xls"
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
      <details><summary>Excel 填写要求</summary>
        <p>使用下载模板的“明细单”，保留第 18–19 行表头，从第 20 行填写商品；一行一个款式，勿合并明细单元格。款号、HS 编码按文本填写，数量、价格和重量只填数字，日期使用 YYYY-MM-DD。</p>
        <p>至少填写款号或品名及数量。中文／英文品名、HS 编码、单位、箱数、重量、单价和金额应按实际资料补齐。公式需在 Excel 中计算并保存后再导入；多工作表会自动识别，上传后须核对预览。</p>
        <p>Excel 提供申报基础资料；证书类型、原产地标准、生产商、申报单位与操作卡等仍须在代理委托／产地证中核对补录。进口方的特殊产地证品名和收货人可在产地证中单独修改。</p>
      </details>
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
