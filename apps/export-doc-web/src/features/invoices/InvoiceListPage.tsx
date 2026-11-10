import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { FileSpreadsheet, FolderOpen, Plus, RefreshCw, Search, Upload, X } from "lucide-react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { PageHeader } from "../../ui/primitives/PageHeader.tsx";
import { FilterBar } from "../../ui/primitives/Field.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import { listPageSizeOptions } from "../../ui/listViewState.ts";
import { InvoiceBatchReportPanel } from "./InvoiceBatchReportPanel.tsx";
import { InvoiceCopyOptionsPanel } from "./InvoiceCopyOptionsPanel.tsx";
import { InvoiceTransferImportPanel } from "./InvoiceTransferImportPanel.tsx";
import { InvoiceTable } from "./InvoiceTable.tsx";
import { SingleWindowActionsPanel } from "./SingleWindowActionsPanel.tsx";
import { useInvoiceListWorkspace } from "./hooks/useInvoiceListWorkspace.ts";
import { ExcelImportModeSelect } from "../tools/excel/ExcelImportModeSelect.tsx";

export function InvoiceListPage({ client }: { client: ExportDocManagerApiClient }) {
  const {
    invoicePermission, excelPermission, singleWindowPermission, batchInvoices, setBatchInvoices,
    invoicePackageExportPermission, workspaceDeviceMode, workspaceDeviceCapabilities, canAuthorizeBatchExport, canBatchExport,
    keyword, setKeyword, committedKeyword, pageNumber, setPageNumber,
    pageSize, copyDraft, closeCopyPanel, transferDraft,
    closeTransferPanel, transferUploadFile, transferSuccessMessage,
    lastExportedPackagePath, excelImportInputRef, navigate, routeSuccessMessage, invoicesQuery,
    singleWindow, handleSearch, handleResetSearch, handlePageSizeChange, copyBusy,
    transferBusy, openCopyPanel, openSingleWindowPanel, patchCopyDraft,
    submitCopyInvoice, handleExportTransferPackage, handleChooseImportTransferPackage, handleChooseExcelImport, handleExcelImportUpload,
    handlePreviewTransferPackage, handlePreviewTransferUpload, patchTransferDraft, submitTransferImport, handleOpenLastExportedPackage,
    invoices, message, isBusy, excelSchemeName, setExcelSchemeName,
  } = useInvoiceListWorkspace({ client });

  return (
    <ListWorkspace  aria-label="发票列表">
      {!invoicePermission.canOperate ? (
        <PermissionNotice>当前权限模板仅允许查看发票；新建、复制、导入和单据包维护已禁用。</PermissionNotice>
      ) : null}
      <WorkspaceDeviceNotice
        mode={workspaceDeviceMode}
        phone="可查看、搜索和打开已有发票进行简单回填；新建完整发票、Excel/单据包导入和批量输出请使用电脑浏览器或桌面端。"
        tablet={workspaceDeviceCapabilities.canImportExport
          ? "可查看、编辑并执行 Excel/单据包导入导出；大批量处理建议使用更宽屏幕。"
          : "可查看、搜索、新建基础草稿和进行轻量编辑；连接鼠标或触控板后可使用 Excel 与单据包导入导出。"}
      />
      <PageHeader title="发票目录" count={invoices?.totalCount} description="查找单据、继续制单，或勾选发票批量输出。" actions={<>
          {workspaceDeviceCapabilities.canImportExport && invoicePermission.canOperate && excelPermission.canOperate ? <button
            className="command-button secondary"
            type="button"
            disabled={isBusy}
            onClick={() => void handleChooseExcelImport()}
          >
            <FileSpreadsheet size={17} aria-hidden="true" />
            <span>导入 Excel</span>
          </button> : null}
          <input
            ref={excelImportInputRef}
            className="visually-hidden"
            aria-label="导入发票 Excel 文件"
            type="file"
            accept=".xlsx,.xlsm,.xltx,.xltm,.xls"
            onChange={(event) => {
              const file = event.currentTarget.files?.[0];
              event.currentTarget.value = "";
              handleExcelImportUpload(file);
            }}
          />
          {workspaceDeviceCapabilities.canImportExport && invoicePermission.canOperate ? <button
            className="command-button secondary"
            type="button"
            disabled={isBusy}
            onClick={() => void handleChooseImportTransferPackage()}
          >
            <Upload size={17} aria-hidden="true" />
            <span>导入单据包</span>
          </button> : null}
          <button
            className="icon-button"
            type="button"
            title="刷新" aria-label="刷新"
            disabled={isBusy}
            onClick={() => void invoicesQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          {invoicePermission.canOperate && workspaceDeviceMode !== "phone" ? <button className="command-button" type="button" onClick={() => navigate("/invoices/new")}>
            <Plus size={17} aria-hidden="true" />
            <span>新建</span>
          </button> : null}
      </>}>
        <FilterBar className="list-workspace-filters" label="筛选发票" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input aria-label="搜索发票" value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="发票号、合同号、客户、港口" />
          <button className="command-button secondary" type="submit" disabled={isBusy}>搜索</button>
          <button className="icon-button" type="button" title="重置搜索" aria-label="重置搜索" disabled={isBusy || (!keyword && !committedKeyword)} onClick={handleResetSearch}><X size={18} aria-hidden="true" /></button>
        </FilterBar>
      </PageHeader>
      {workspaceDeviceCapabilities.canImportExport && invoicePermission.canOperate && excelPermission.canOperate &&
        <details className="form-section"><summary>{excelSchemeName ? `Excel 导入：手动 · ${excelSchemeName}` : "特殊格式 Excel 导入"}</summary>
          <ExcelImportModeSelect client={client} value={excelSchemeName} onChange={setExcelSchemeName} disabled={isBusy} />
        </details>}

      {message ? <InlineNotice tone="error" title="发票操作未完成">{message}</InlineNotice> : null}
      {routeSuccessMessage ? <InlineNotice tone="success">{routeSuccessMessage}</InlineNotice> : null}
      {transferSuccessMessage ? (
        <InlineNotice
          tone="success"
          action={lastExportedPackagePath && isDesktopBridgeAvailable() ? (
            <button className="text-button compact-text-button" type="button" onClick={() => void handleOpenLastExportedPackage()}>
              <FolderOpen size={15} aria-hidden="true" />
              <span>打开</span>
            </button>
          ) : undefined}
        >
          {transferSuccessMessage}
        </InlineNotice>
      ) : null}
      {copyDraft ? (
        <InvoiceCopyOptionsPanel
          draft={copyDraft}
          isBusy={copyBusy}
          onCancel={closeCopyPanel}
          onChange={patchCopyDraft}
          onSubmit={submitCopyInvoice}
        />
      ) : null}
      {transferDraft ? (
        <InvoiceTransferImportPanel
          draft={transferDraft}
          isBusy={transferBusy}
          uploadMode={!isDesktopBridgeAvailable()}
          uploadFile={transferUploadFile}
          onUploadFileChange={handlePreviewTransferUpload}
          onCancel={closeTransferPanel}
          onChange={patchTransferDraft}
          onPreview={handlePreviewTransferPackage}
          onSubmit={submitTransferImport}
        />
      ) : null}
      {singleWindow.draft ? (
        <SingleWindowActionsPanel
          draft={singleWindow.draft}
          isBusy={singleWindow.isActionBusy}
          message={singleWindow.message}
          messageType={singleWindow.messageType}
          jobId={singleWindow.jobId}
          packagePath={singleWindow.packagePath}
          review={singleWindow.review}
          reviewBusinessType={singleWindow.reviewBusinessType}
          reviewInvoiceId={singleWindow.reviewInvoiceId}
          stationAssignmentCode={singleWindow.stationAssignmentCode}
          isReviewBusy={singleWindow.isReviewBusy}
          canOperate={singleWindowPermission.canOperate}
          canExportBookingSheet={excelPermission.canOperate}
          onCancel={() => {
            singleWindow.close();
          }}
          onEditCustomsCoo={(invoice) => navigate(`/single-window/coo/${invoice.id}`)}
          onEditAgentConsignment={(invoice) => navigate(`/single-window/acd/${invoice.id}`)}
          onReviewCustomsCoo={(invoice) => singleWindow.buildReview(invoice, "CustomsCoo")}
          onReviewAgentConsignment={(invoice) => singleWindow.buildReview(invoice, "AgentConsignment")}
          onRepairReview={singleWindow.repairReview}
          onStationAssignmentCodeChange={singleWindow.setStationAssignmentCode}
          onExportCustomsCoo={(invoice) => void singleWindow.exportPackage(invoice, "CustomsCoo")}
          onExportAgentConsignment={(invoice) => void singleWindow.exportPackage(invoice, "AgentConsignment")}
          onImportReceiptPackage={() => void singleWindow.importReceipt()}
          onOpenOperationCenter={() => navigate("/single-window/operation-center")}
          onOpenPackagePath={() => void singleWindow.openPackagePath()}
          onExportBookingSheet={(invoice) => void singleWindow.exportBookingSheet(invoice)}
        />
      ) : null}

      {canAuthorizeBatchExport && <InvoiceBatchReportPanel hidden={!canBatchExport} client={client} selected={batchInvoices} onChange={setBatchInvoices} />}
      <InvoiceTable
        selection={canBatchExport ? { items: batchInvoices, onChange: setBatchInvoices } : undefined}
        data={invoices?.items ?? []}
        isBusy={isBusy}
        hasError={Boolean(invoicesQuery.isError)}
        canOperate={invoicePermission.canOperate && workspaceDeviceMode !== "phone"}
        canExportPackage={invoicePackageExportPermission.allowed && workspaceDeviceCapabilities.canImportExport}
        canExportBookingSheet={excelPermission.canOperate && workspaceDeviceCapabilities.canImportExport}
        canUseSingleWindow={singleWindowPermission.canView}
        onOpen={(invoiceId) => navigate(`/invoices/${invoiceId}`)}
        onCopy={openCopyPanel}
        onExportPackage={(invoice) => void handleExportTransferPackage(invoice)}
        onExportBookingSheet={(invoice) => void singleWindow.exportBookingSheet(invoice)}
        onSingleWindow={openSingleWindowPanel}
      />

      <Pagination
        pageNumber={invoices?.pageNumber ?? pageNumber}
        totalPages={Math.max(invoices?.totalPages ?? 1, 1)}
        totalCount={invoices?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </ListWorkspace>
  );
}
