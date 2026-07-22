import {
ApiSingleWindowHandoffPackageResponse,
ApiSingleWindowImportedPackageResponse,
SingleWindowReceiptCollectionResult,
SingleWindowReceiptParseResult
} from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { formatPlainNumber } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

import {
formatBatchStatus,
formatDateTime,
formatPackageType,
formatParsedBusinessType,
formatParsedReceiptKind,
formatParsedReceiptStatus,
readDisplayText
} from "./singleWindowOperationCenterModel.ts";

import { DetailItem } from "./SingleWindowOperationCenterTables.tsx";


export function ReceiptCollectionResultSummary({
  result,
  onOpenError,
}: {
  result: SingleWindowReceiptCollectionResult;
  onOpenError: (message: string) => void;
}) {
  return (
    <>
      <div className="detail-grid handoff-result-grid">
        <DetailItem label="批次 ID" value={result.batchId} />
        <DetailItem label="批次号" value={result.batchReference} />
        <DetailItem label="回执文件" value={result.receiptFiles.length} />
        <DetailItem
          label="回执目录"
          value={result.receiptRootPath}
          wide
          actions={renderOpenPathAction(result.receiptRootPath, "打开回执目录", onOpenError)}
        />
      </div>
      <ResponsiveTableFrame label="单一窗口收件包结果" className="compact-table receipt-package-result-table" mobileLayout="scroll">
        <table>
          <thead>
            <tr>
              <th>序号</th>
              <th>回执文件</th>
            </tr>
          </thead>
          <tbody>
            {result.receiptFiles.length === 0 ? (
              <tr>
                <td colSpan={2} className="empty-cell small-empty">
                  暂无数据
                </td>
              </tr>
            ) : (
              result.receiptFiles.map((filePath, index) => (
                <tr key={`${filePath}-${index}`}>
                  <td>{formatPlainNumber(index + 1)}</td>
                  <td className="path-cell" title={filePath}>
                    {filePath}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </>
  );
}

export function ReceiptPackageExportResult({
  result,
  onOpenError,
}: {
  result: ApiSingleWindowHandoffPackageResponse;
  onOpenError: (message: string) => void;
}) {
  const manifest = result.manifest;

  return (
    <>
      <div className="detail-grid handoff-result-grid">
        <DetailItem label="批次 ID" value={result.trackingBatchId ?? "-"} />
        <DetailItem label="批次号" value={manifest.batchReference} />
        <DetailItem label="业务" value={formatParsedBusinessType(manifest.businessType)} />
        <DetailItem label="回执文件" value={manifest.payloadFiles.length} />
        <DetailItem label="警告" value={manifest.warnings.length} />
        <DetailItem
          label="包路径"
          value={result.packagePath}
          wide
          actions={renderOpenPathAction(result.packagePath, "打开回执包位置", onOpenError)}
        />
      </div>
    </>
  );
}

export function ReceiptPackageImportResult({
  result,
  onOpenError,
}: {
  result: ApiSingleWindowImportedPackageResponse;
  onOpenError: (message: string) => void;
}) {
  return <PackageImportResult result={result} includeReceipts onOpenError={onOpenError} />;
}

export function PackageImportResult({
  result,
  includeReceipts,
  onOpenError,
}: {
  result: ApiSingleWindowImportedPackageResponse;
  includeReceipts: boolean;
  onOpenError: (message: string) => void;
}) {
  const manifest = result.manifest;

  return (
    <>
      <div className="detail-grid handoff-result-grid">
        <DetailItem label="批次 ID" value={result.trackingBatchId ?? "-"} />
        <DetailItem label="批次号" value={manifest.batchReference} />
        <DetailItem label="包类型" value={formatPackageType(manifest.packageType)} />
        <DetailItem label="业务" value={formatParsedBusinessType(manifest.businessType)} />
        <DetailItem label="跟踪状态" value={formatBatchStatus(result.trackingStatus)} />
        <DetailItem label="负载文件" value={manifest.payloadFiles.length} />
        <DetailItem label="附件文件" value={manifest.attachmentFiles.length} />
        <DetailItem label="警告" value={manifest.warnings.length} />
        {includeReceipts ? <DetailItem label="解析回执" value={result.parsedReceipts.length} /> : null}
        {includeReceipts ? <DetailItem label="写入回执" value={result.persistedReceiptCount} /> : null}
        <DetailItem label="保留工作目录" value={result.workingDirectoryKept ? "是" : "否"} />
        <DetailItem
          label="包路径"
          value={result.packagePath}
          wide
          actions={renderOpenPathAction(result.packagePath, "打开包路径", onOpenError)}
        />
        <DetailItem
          label="工作目录"
          value={result.workingDirectory}
          wide
          actions={renderOpenPathAction(result.workingDirectory, "打开工作目录", onOpenError)}
        />
      </div>
      {includeReceipts ? <ParsedReceiptTable data={result.parsedReceipts} /> : null}
    </>
  );
}

export function ParsedReceiptTable({ data }: { data: SingleWindowReceiptParseResult[] }) {
  return (
    <ResponsiveTableFrame label="单一窗口回执解析结果" className="compact-table receipt-package-result-table" mobileLayout="scroll">
      <table>
        <thead>
          <tr>
            <th>类型</th>
            <th>状态</th>
            <th>回执码</th>
            <th>消息</th>
            <th>参考号</th>
            <th>来源文件</th>
            <th>发生</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={7} className="empty-cell small-empty">
                暂无数据
              </td>
            </tr>
          ) : (
            data.map((receipt, index) => (
              <tr key={`${receipt.sourceFileName}-${receipt.receiptCode}-${index}`}>
                <td>{formatParsedReceiptKind(receipt.receiptKind)}</td>
                <td>
                  <span className="status-pill">{formatParsedReceiptStatus(receipt.businessStatus)}</span>
                </td>
                <td>{readDisplayText(receipt.receiptCode)}</td>
                <td className="message-cell" title={receipt.receiptMessage ?? ""}>
                  {readDisplayText(receipt.receiptMessage)}
                </td>
                <td>{readDisplayText(receipt.referenceNo)}</td>
                <td className="path-cell" title={receipt.sourceFileName}>
                  {readDisplayText(receipt.sourceFileName)}
                </td>
                <td>{formatDateTime(receipt.occurredAt)}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}

