import { Eye, Printer, RefreshCw } from "lucide-react";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
type Props={canPreview:boolean;canPrint:boolean;canRefreshTemplates:boolean;errorMessage:string|null;hasSavedInvoice:boolean;hasUnsavedDraftChanges:boolean;isBusy:boolean;jobId:string|null;previewStoragePolicy:string|null;statusMessage:string|null;templateMessage:string|null;onPreview():void;onPrint():void;onRefresh():void};
export function InvoiceReportPreviewHeader(p:Props){const {canPreview,errorMessage,hasSavedInvoice,hasUnsavedDraftChanges,isBusy,jobId:lastCreatedJobId,previewStoragePolicy,statusMessage,templateMessage}=p;const canPrintPreview=p.canPrint;return (<>
      <div className="section-header">
        <h2>报表预览</h2>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新模板" aria-label="刷新模板"
            disabled={!p.canRefreshTemplates || isBusy}
            onClick={p.onRefresh}
          >
            <RefreshCw size={17} aria-hidden="true" />
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={isBusy || !canPreview}
            onClick={p.onPreview}
          >
            <Eye size={17} aria-hidden="true" />
            <span>预览</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            title="打印当前预览"
            disabled={!canPrintPreview}
            onClick={p.onPrint}
          >
            <Printer size={17} aria-hidden="true" />
            <span>打印</span>
          </button>
        </div>
      </div>

      {templateMessage ? <InlineNotice tone="warning" title="报表模板提示">{templateMessage}</InlineNotice> : null}
      {errorMessage ? <InlineNotice tone="error" title="发票报表生成失败">{errorMessage}</InlineNotice> : null}
      {statusMessage ? (
        <InlineNotice tone="success" action={<ViewJobButton jobId={lastCreatedJobId} disabled={isBusy} />}>
          {statusMessage}
        </InlineNotice>
      ) : null}
      {hasSavedInvoice && hasUnsavedDraftChanges ? (
        <InlineNotice tone="info">当前发票有未保存修改。HTML 预览使用当前草稿；PDF、托单、单据包和邮件请先保存后再生成。</InlineNotice>
      ) : null}
      {previewStoragePolicy ? <InlineNotice tone="info">{previewStoragePolicy}</InlineNotice> : null}

</>);}
