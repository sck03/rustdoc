import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Boxes } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ExportDocManagerApiClient,
  SingleWindowOperationCenterDetail,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import {
  formatBatchStatus,
  formatBusinessType,
  formatDateTime,
} from "./singleWindowOperationCenterModel.ts";
import {
  DetailItem,
  PackageRecordTable,
  ReceiptRecordTable,
} from "./SingleWindowOperationCenterTables.tsx";

export function SingleWindowOperationCenterDetailPage({
  client,
}: {
  client: ExportDocManagerApiClient;
}) {
  const permission = useModulePermission("document.single-window");
  const { batchId } = useParams();
  const navigate = useNavigate();
  const parsedBatchId = Number(batchId);
  const isBatchIdValid = Number.isInteger(parsedBatchId) && parsedBatchId > 0;

  const detailQuery = useQuery({
    queryKey: queryKeys.singleWindowOperationCenterDetail(parsedBatchId),
    queryFn: () => client.getSingleWindowOperationCenterDetail({ batchId: parsedBatchId }),
    enabled: isBatchIdValid,
  });

  const detail = detailQuery.data ?? null;
  const message = !isBatchIdValid
    ? "批次编号无效。"
    : detailQuery.isError
      ? readApiError(detailQuery.error)
      : null;

  return (
    <section className="editor-surface single-window-detail-surface" aria-label="单一窗口批次详情">
      <div className="editor-toolbar">
        <button
          className="command-button secondary"
          type="button"
          onClick={() => navigate("/single-window/operation-center")}
        >
          <ArrowLeft size={17} aria-hidden="true" />
          <span>返回操作中心</span>
        </button>
        <div className="editor-title">
          <Boxes size={18} aria-hidden="true" />
          <span>{detail ? detail.batchReference || "批次详情" : "批次详情"}</span>
        </div>
      </div>

      {message ? <InlineNotice tone="error" title="批次详情加载失败">{message}</InlineNotice> : null}
      {!permission.canOperate ? (
        <PermissionNotice>当前权限仅允许查看批次、提交包和回执记录。</PermissionNotice>
      ) : null}
      {!detail && detailQuery.isFetching ? (
        <PageState
          tone="loading"
          title="正在加载批次详情"
          description="请稍候，系统正在读取业务状态和回执记录。"
        />
      ) : null}

      {detail ? <OperationCenterDetail detail={detail} /> : null}
    </section>
  );
}

function OperationCenterDetail({ detail }: { detail: SingleWindowOperationCenterDetail }) {
  return (
    <div className="entity-form">
      <section className="form-section" aria-label="批次信息">
        <div className="section-header">
          <h2>业务概览</h2>
        </div>
        <div className="detail-grid">
          <DetailItem label="公司抬头" value={detail.companyScope} wide />
          <DetailItem label="业务类型" value={formatBusinessType(detail.businessType)} />
          <DetailItem label="当前状态" value={formatBatchStatus(detail.status)} />
          <DetailItem label="发票号" value={detail.invoiceNo} />
          <DetailItem label="合同号" value={detail.contractNo} />
          <DetailItem label="业务参考号" value={detail.referenceNo} />
          <DetailItem label="提交版本" value={detail.submissionVersion} />
          <DetailItem label="草稿版本" value={detail.draftRevision} />
          <DetailItem label="持卡机档案" value={detail.clientProfileName} />
          <DetailItem label="操作卡标识" value={detail.assignedCardIdentifier} />
          <DetailItem label="报文数量" value={detail.payloadFileCount} />
          <DetailItem label="附件数量" value={detail.attachmentFileCount} />
          <DetailItem label="待确认事项" value={detail.warningCount} />
          <DetailItem label="创建时间" value={formatDateTime(detail.createdAt)} />
          <DetailItem label="更新时间" value={formatDateTime(detail.updatedAt)} />
          <DetailItem label="写入 OutBox 时间" value={formatDateTime(detail.lastClientDispatchAt)} />
          <DetailItem label="最近回执时间" value={formatDateTime(detail.lastReceiptAt)} />
        </div>
      </section>

      <section className="form-section" aria-label="提交包记录">
        <div className="section-header">
          <h2>提交包记录</h2>
          <span className="section-count">{detail.packageRecords.length} 条</span>
        </div>
        <PackageRecordTable data={detail.packageRecords} />
      </section>

      <section className="form-section" aria-label="回执记录">
        <div className="section-header">
          <h2>回执记录</h2>
          <span className="section-count">{detail.receiptRecords.length} 条</span>
        </div>
        <ReceiptRecordTable data={detail.receiptRecords} />
      </section>

      <InlineNotice tone="info" title="操作说明">
        提交包导入、写入当前档案交接 OutBox、收集回执和导出回执包均在操作中心列表完成；官方客户端的导入与提交仍由操作员确认，详情页只保留业务审计信息。
      </InlineNotice>
    </div>
  );
}
