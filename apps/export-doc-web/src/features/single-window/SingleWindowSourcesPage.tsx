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
import { SingleWindowImportPreview } from "./SingleWindowImportPreview.tsx";
import { ExcelImportModeSelect } from "../tools/excel/ExcelImportModeSelect.tsx";
import "../../styles/routes/single-window.css";

export function SingleWindowSourcesPage({ client }: { client: ExportDocManagerApiClient }) {
  const work = useSingleWindowSources(client);
  if (!work.permission.canView) return <PermissionNotice>没有申报资料查看权限。</PermissionNotice>;
  const imported = work.preview.preview?.result;
  return <RecordWorkspace title="申报资料" className="single-window-surface">
    <SingleWindowTabs activeKey="sources" />
    {work.message && <InlineNotice tone="error">{work.message}</InlineNotice>}
    {work.success && <InlineNotice tone="success">{work.success}</InlineNotice>}
    {work.permission.canOperate && <form className="form-section single-window-source-form" onSubmit={event => { event.preventDefault(); void work.create(); }}>
      <h2>新建申报资料</h2>
      <div className="single-window-source-import">
        <TextField label="资料编号 / 客户发票号" value={work.referenceNo} onChange={work.setReferenceNo} required disabled={work.busy} />
        <label className="form-field"><span className="form-field-label">客户 Excel（可选）</span><FileInput disabled={work.busy} selectedFileName={work.fileName} accept=".xlsx,.xlsm,.xltx,.xltm,.xls"
          onChange={event => { work.select(event.currentTarget.files?.[0]); event.currentTarget.value = ""; }} /></label>
        <ExcelImportModeSelect client={client} value={work.schemeName} onChange={work.changeScheme} disabled={work.busy} />
      </div>
      <div className="form-actions">
        <div className="toolbar-actions">
          <button type="button" className="command-button secondary" disabled={work.busy || !work.canDownloadTemplate} title={work.canDownloadTemplate ? undefined : "需要文件任务的查看和操作权限"} onClick={() => void work.downloadTemplate()}>下载产地证 Excel 模板</button>
          {work.fileName && <button className="command-button secondary" type="button" disabled={work.busy} onClick={work.clearFile}>清除文件，手工录入</button>}
        </div>
        <button className="command-button" type="submit" disabled={work.busy || !work.referenceNo.trim() || Boolean(work.fileName && !imported?.success)}>创建申报资料</button>
      </div>
      {work.preview.preview && <SingleWindowImportPreview preview={work.preview.preview} />}
      <details><summary>Excel 填写要求</summary>
        <p>不选择 Excel 可手工录入。产地证模板包含“基本资料”“商品明细”“生产企业”；保留字段和表头，从第 4 行填写商品和企业，以企业编号关联。一家工厂只填一次。</p>
        <p>编号、HS 编码、组织机构代码及电话按文本填写；数量、重量、金额填数字，日期为 YYYY-MM-DD。FOB 值单独填写，不等同于 CIF 发票金额。公式须计算并保存后再导入。</p>
        <p>商品表只采集基础货物资料。原产地标准、证书货物描述、商品原产国和中文单位由操作员导入后核对；描述可由英文品名、包装数量和单位组合。</p>
        <p>证书类型、原产标准及生产商描述须按实际证书核对；签证机构、申报单位与操作卡在编辑页补齐。其他客户格式可使用识别方案，工厂等未识别资料须在编辑页补录。</p>
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
