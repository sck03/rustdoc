import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { Button } from "../../ui/Button.tsx";
import { PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { PageHeader } from "../../ui/primitives/PageHeader.tsx";
import { FilterBar } from "../../ui/primitives/Field.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback } from "../../ui/OperationFeedback.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";
import { opportunityStages } from "./salesOpportunityModel.ts";
import { SalesOpportunityEditor } from "./SalesOpportunityEditor.tsx";
import { SalesOpportunityHistory } from "./SalesOpportunityHistory.tsx";
import { useSalesOpportunityWorkspace } from "./useSalesOpportunityWorkspace.ts";

export function SalesOpportunityPage({ businessTimeZone, client }: { businessTimeZone: string; client: ExportDocManagerApiClient }) {
  const model = useSalesOpportunityWorkspace(client);
  const { directory, page, record, permissions } = model;
  return <ListWorkspace>
    <OperationFeedback feedback={model.feedback} />
    {!permissions.create && !permissions.edit && !permissions.transition && <PermissionNotice>当前权限允许查看商机。</PermissionNotice>}
    {model.view === "directory" ? <section className="form-section" aria-label="商机目录">
      <PageHeader title="商机目录" count={page.data?.totalCount} unit="项" actions={permissions.create && <Button variant="primary" disabled={model.busy} onClick={() => model.navigate(null, "editor")}>新建商机</Button>}>
        <FilterBar className="list-workspace-filters" label="筛选商机目录" onSubmit={event => { event.preventDefault(); directory.setKeyword(directory.keywordInput.trim()); directory.setPageNumber(1); }}>
          <input aria-label="搜索商机目录" value={directory.keywordInput} onChange={event => directory.setKeywordInput(event.target.value)} placeholder="商机、客户、产品或报价编号" />
          <select aria-label="商机阶段" value={directory.status} onChange={event => { directory.setStatus(event.target.value); directory.setPageNumber(1); }}><option value="">全部阶段</option>{opportunityStages.map(stage => <option key={stage}>{stage}</option>)}</select>
          <label className="inline-check"><input type="checkbox" checked={model.includeArchived} onChange={event => model.changeArchived(event.target.checked)} />含归档</label>
          <Button type="submit">搜索</Button>
        </FilterBar>
      </PageHeader>
      {page.isError ? <PageState tone="error" title="商机加载失败" description={readApiError(page.error)} /> : <>
        <ResponsiveTableFrame label="销售商机列表" mobileLayout="scroll" busy={page.isFetching}><table className="data-table responsive-data-table"><thead><tr><th>商机</th><th>客户</th><th>产品</th><th>金额</th><th>概率</th><th>阶段</th><th>操作</th></tr></thead><tbody>
          {page.data?.items.map(item => <tr key={item.id}>
            <td><TablePrimaryText value={item.title} secondary={item.quotationNo} /></td><td>{item.customerName}</td><td>{item.productCode || item.productName || "—"}</td>
            <td>{item.currency} {item.estimatedAmount.toFixed(2)}</td><td>{item.probabilityPercent}%</td><td>{item.isArchived ? "已归档" : <BusinessStatusBadge value={item.stage} />}</td>
            <td><Button disabled={model.busy} onClick={() => model.navigate(item.id, item.isArchived ? "history" : "editor")}>{item.isArchived ? "查看历史" : permissions.edit ? "编辑" : "查看"}</Button></td>
          </tr>)}
        </tbody></table></ResponsiveTableFrame>
        {!page.isFetching && !page.data?.items.length && <PageState title="暂无商机记录" />}
      </>}
      <Pagination pageNumber={directory.pageNumber} totalPages={page.data?.totalPages ?? 1} totalCount={page.data?.totalCount ?? 0} pageSize={directory.pageSize} pageSizeOptions={[20, 30, 50, 100]} isBusy={page.isFetching}
        onPageChange={directory.setPageNumber} onPageSizeChange={value => { directory.setPageSize(value); directory.setPageNumber(1); }} />
    </section> : <>
      <PageHeader title={record?.title || (model.id ? "商机详情" : "新建商机")} description={record?.customerName}
        actions={<Button disabled={model.busy} onClick={() => model.navigate(model.id, "directory")}>返回商机目录</Button>} />
      {record && <TaskViewTabs idPrefix="sales-opportunity-workspace" value={model.view} label="商机详情" onChange={view => { if (!model.busy) model.navigate(model.id, view); }}
        items={[{ id: "editor", label: "商机资料" }, { id: "history", label: "报价与阶段历史" }]} />}
      <div {...(record ? getTaskViewPanelProps("sales-opportunity-workspace", model.view) : {})}>
      {model.id && (model.detail.isPending || model.detail.isError || !record) ? <PageState tone={model.detail.isError ? "error" : "loading"} title={model.detail.isError ? "商机加载失败" : "正在读取商机"}
        description={model.detail.isError ? readApiError(model.detail.error) : undefined} action={model.detail.isError ? <Button onClick={model.reload}>重新加载</Button> : undefined} />
        : model.view === "history" && model.id ? <SalesOpportunityHistory key={model.id} id={model.id} client={client} timeZone={businessTimeZone} />
          : <SalesOpportunityEditor key={model.id ?? "new"} model={model} />}
      </div>
    </>}
  </ListWorkspace>;
}
