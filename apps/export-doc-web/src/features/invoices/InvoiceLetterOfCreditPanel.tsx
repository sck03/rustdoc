import { useEffect, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { ChevronDown, ChevronUp, FileText, ShieldCheck, Upload } from "lucide-react";
import { ApiInvoiceDetailDto, ApiLetterOfCreditReviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import {
  isDesktopBridgeAvailable,
  selectLetterOfCreditFile,
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { FieldShell, TextField } from "../../ui/FormFields.tsx";
import { PathField, PathTextAreaField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { normalizeInvoiceForSave } from "./invoiceModel.ts";

type LetterOfCreditImportSource =
  | { kind: "desktop"; filePath: string }
  | { kind: "browser"; file: File };

const maximumLetterOfCreditBytes = 25 * 1024 * 1024;
const letterOfCreditAccept = ".pdf,.txt,.md,.csv,.json,.xml,.png,.jpg,.jpeg,.bmp,.gif,.tif,.tiff,.webp";

export function InvoiceLetterOfCreditPanel({
  client,
  invoice,
  disabled,
  reviewDisabled,
  onChange,
  onClearPageMessages,
  onBusyChange,
}: {
  client: ExportDocManagerApiClient;
  invoice: ApiInvoiceDetailDto;
  disabled?: boolean;
  reviewDisabled?: boolean;
  onChange: (next: Partial<ApiInvoiceDetailDto>) => void;
  onClearPageMessages: () => void;
  onBusyChange?: (isBusy: boolean) => void;
}) {
  const [importMessage, setImportMessage] = useState<string | null>(null);
  const [importMessageType, setImportMessageType] = useState<"success" | "error" | null>(null);
  const [reviewMessage, setReviewMessage] = useState<string | null>(null);
  const [reviewResult, setReviewResult] = useState<ApiLetterOfCreditReviewResponse | null>(null);
  const [browserFile, setBrowserFile] = useState<File | null>(null);
  const [isExpanded, setIsExpanded] = useState(false);
  const desktopAvailable = isDesktopBridgeAvailable();

  const importMutation = useMutation({
    mutationFn: (source: LetterOfCreditImportSource) => source.kind === "desktop"
      ? client.importLetterOfCreditDocument({ body: { filePath: source.filePath } })
      : client.uploadLetterOfCreditDocument({ fileName: source.file.name, body: source.file }),
    onSuccess: (response) => {
      onChange({
        letterOfCreditSourcePath: response.sourcePath,
        letterOfCreditContent: response.extractedText,
      });
      onClearPageMessages();
      clearReviewState();
      setImportMessage(`信用证已导入：${response.sourceDescription}`);
      setImportMessageType("success");
    },
    onError: (error) => {
      setImportMessage(readApiError(error));
      setImportMessageType("error");
      onClearPageMessages();
    },
  });

  const reviewMutation = useMutation({
    mutationFn: () =>
      client.reviewLetterOfCreditCompliance({
        body: {
          invoice: normalizeInvoiceForSave(invoice, invoice.id ?? 0),
        },
      }),
    onSuccess: (response) => {
      setReviewResult(response);
      setReviewMessage(null);
      onClearPageMessages();
      clearImportMessage();
    },
    onError: (error) => {
      setReviewResult(null);
      setReviewMessage(readApiError(error));
      onClearPageMessages();
    },
  });

  const isImporting = importMutation.isPending;
  const isReviewing = reviewMutation.isPending;
  const hasReviewContext = Boolean(
    invoice.letterOfCreditContent?.trim() ||
      invoice.letterOfCreditNo?.trim() ||
      invoice.specialTerms?.trim(),
  );
  const hasCreditData = Boolean(
    invoice.letterOfCreditNo?.trim() ||
      invoice.letterOfCreditSourcePath?.trim() ||
      invoice.letterOfCreditContent?.trim() ||
      invoice.specialTerms?.trim(),
  );
  const hasImportSource = desktopAvailable
    ? Boolean(invoice.letterOfCreditSourcePath?.trim())
    : Boolean(browserFile);

  useEffect(() => {
    onBusyChange?.(isImporting || isReviewing);
    return () => onBusyChange?.(false);
  }, [isImporting, isReviewing, onBusyChange]);

  useEffect(() => {
    setBrowserFile(null);
    clearImportMessage();
    clearReviewState();
  }, [invoice.id]);

  function clearImportMessage() {
    setImportMessage(null);
    setImportMessageType(null);
  }

  function showImportError(value: string) {
    setImportMessage(value);
    setImportMessageType("error");
  }

  function clearReviewState() {
    setReviewMessage(null);
    setReviewResult(null);
  }

  function patchInvoice(next: Partial<ApiInvoiceDetailDto>) {
    if (disabled) {
      return;
    }

    onChange(next);
    clearImportMessage();
    clearReviewState();
  }

  async function chooseLetterOfCreditFile() {
    if (disabled) {
      return;
    }

    try {
      const selected = await selectLetterOfCreditFile();
      if (selected) {
        setBrowserFile(null);
        onChange({ letterOfCreditSourcePath: selected });
        onClearPageMessages();
        clearImportMessage();
      }
    } catch (error) {
      showImportError(readDesktopError(error));
    }
  }

  function importLetterOfCredit() {
    if (disabled) {
      return;
    }

    onClearPageMessages();
    clearImportMessage();
    if (desktopAvailable) {
      const filePath = invoice.letterOfCreditSourcePath?.trim() ?? "";
      if (!filePath) {
        showImportError("请选择或输入信用证来源文件。");
        return;
      }

      importMutation.mutate({ kind: "desktop", filePath });
      return;
    }

    if (!browserFile) {
      showImportError("请选择要上传的信用证文件。");
      return;
    }

    if (browserFile.size <= 0 || browserFile.size > maximumLetterOfCreditBytes) {
      showImportError("信用证文件不能为空，且不能超过 25 MB。");
      return;
    }

    importMutation.mutate({ kind: "browser", file: browserFile });
  }

  function selectBrowserFile(file: File | null) {
    if (disabled) {
      return;
    }

    clearImportMessage();
    clearReviewState();
    if (!file) {
      setBrowserFile(null);
      return;
    }

    if (file.size <= 0 || file.size > maximumLetterOfCreditBytes) {
      setBrowserFile(null);
      showImportError("信用证文件不能为空，且不能超过 25 MB。");
      return;
    }

    setBrowserFile(file);
  }

  function reviewLetterOfCredit() {
    if (reviewDisabled || isReviewing || isImporting) {
      return;
    }

    if (!hasReviewContext) {
      setReviewMessage("请先导入信用证文本，或至少补充信用证号/信用证要求后再进行审查。");
      setReviewResult(null);
      return;
    }

    onClearPageMessages();
    clearImportMessage();
    setReviewMessage(null);
    reviewMutation.mutate();
  }

  return (
    <section className="form-section letter-of-credit-section information-tier-advanced" aria-label="信用证">
      <div className="section-header">
        <div className="letter-of-credit-heading">
          <h2>信用证</h2>
          <span className="section-description">{hasCreditData ? "已填写，点击展开查看" : "低频信息，按需展开"}</span>
        </div>
        <div className="toolbar-actions">
          <button
            className="secondary-button compact-command-button"
            type="button"
            aria-expanded={isExpanded}
            disabled={isImporting || isReviewing}
            onClick={() => setIsExpanded((current) => !current)}
          >
            {isExpanded ? <ChevronUp size={16} aria-hidden="true" /> : <ChevronDown size={16} aria-hidden="true" />}
            <span>{isExpanded ? "收起信用证" : "展开信用证"}</span>
          </button>
        </div>
      </div>
      {isExpanded ? (
        <>
          <div className="toolbar-actions letter-of-credit-actions">
            <button
              className="command-button secondary"
              type="button"
              disabled={disabled || isImporting || !hasImportSource}
              onClick={importLetterOfCredit}
            >
              <Upload size={17} aria-hidden="true" />
              <span>导入信用证</span>
            </button>
            <button
              className="command-button secondary"
              type="button"
              disabled={reviewDisabled || isImporting || isReviewing || !hasReviewContext}
              onClick={reviewLetterOfCredit}
            >
              <ShieldCheck size={17} aria-hidden="true" />
              <span>{isReviewing ? "审查中" : "AI 审查"}</span>
            </button>
          </div>
          {importMessage ? (
            <InlineNotice tone={importMessageType === "error" ? "error" : "success"}>{importMessage}</InlineNotice>
          ) : null}
          {reviewMessage ? <InlineNotice tone="warning" title="信用证审查提示">{reviewMessage}</InlineNotice> : null}
          <div className="field-grid">
            <TextField
              label="信用证号"
              value={invoice.letterOfCreditNo ?? ""}
              disabled={disabled}
              onChange={(value) => patchInvoice({ letterOfCreditNo: value })}
            />
          </div>
          {desktopAvailable ? (
            <PathField
              label="来源文件"
              value={invoice.letterOfCreditSourcePath ?? ""}
              disabled={disabled || isImporting}
              onChange={(value) => patchInvoice({ letterOfCreditSourcePath: value })}
              actions={
                <>
                  <DesktopIconButton title="选择信用证文件" disabled={disabled || isImporting} onClick={chooseLetterOfCreditFile}>
                    <FileText size={15} aria-hidden="true" />
                  </DesktopIconButton>
                  {renderOpenPathAction(invoice.letterOfCreditSourcePath, "打开信用证来源", showImportError)}
                </>
              }
            />
          ) : (
            <FieldShell
              label="上传信用证文件"
              disabled={disabled || isImporting}
              description={browserFile
                ? `待导入：${browserFile.name}`
                : invoice.letterOfCreditSourcePath?.trim()
                  ? `已记录原文件名：${invoice.letterOfCreditSourcePath}`
                  : "浏览器只上传当前选择文件；服务端临时文件会立即删除，发票草稿仅保存原文件名。"}
            >
              {(descriptionId) => (
                <input
                  key={invoice.id ?? 0}
                  type="file"
                  accept={letterOfCreditAccept}
                  disabled={disabled || isImporting}
                  aria-describedby={descriptionId}
                  onChange={(event) => {
                    const selectedFile = event.currentTarget.files?.[0] ?? null;
                    event.currentTarget.value = "";
                    selectBrowserFile(selectedFile);
                  }}
                />
              )}
            </FieldShell>
          )}
          <PathTextAreaField
            label="信用证文本"
            value={invoice.letterOfCreditContent ?? ""}
            disabled={disabled || isImporting}
            onChange={(value) => patchInvoice({ letterOfCreditContent: value })}
          />
          {reviewResult ? (
            <div className="letter-of-credit-review-result">
              <div className="letter-of-credit-review-meta">
                <span>{reviewResult.contextSummary}</span>
                {reviewResult.letterOfCreditContentTruncated ? <strong>信用证文本已截断</strong> : null}
              </div>
              <textarea value={reviewResult.reportText} readOnly />
            </div>
          ) : null}
        </>
      ) : null}
    </section>
  );
}
