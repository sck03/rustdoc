import { useEffect, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { FileText, ShieldCheck, Upload } from "lucide-react";
import { ApiInvoiceDetailDto, ApiLetterOfCreditReviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import {
  isDesktopBridgeAvailable,
  selectLetterOfCreditFile,
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { TextField } from "../../ui/FormFields.tsx";
import { PathField, PathTextAreaField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { normalizeInvoiceForSave } from "./invoiceModel.ts";

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
  const desktopAvailable = isDesktopBridgeAvailable();

  const importMutation = useMutation({
    mutationFn: (filePath: string) =>
      client.importLetterOfCreditDocument({
        body: { filePath },
      }),
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

  useEffect(() => {
    onBusyChange?.(isImporting || isReviewing);
    return () => onBusyChange?.(false);
  }, [isImporting, isReviewing, onBusyChange]);

  useEffect(() => {
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

    const filePath = invoice.letterOfCreditSourcePath?.trim() ?? "";
    if (!filePath) {
      showImportError("请选择或输入信用证来源文件。");
      return;
    }

    onClearPageMessages();
    clearImportMessage();
    importMutation.mutate(filePath);
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
    <section className="form-section letter-of-credit-section" aria-label="信用证">
      <div className="section-header">
        <h2>信用证</h2>
        <div className="toolbar-actions">
          <button
            className="command-button secondary"
            type="button"
            disabled={disabled || isImporting || !invoice.letterOfCreditSourcePath?.trim()}
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
      <PathField
        label="来源文件"
        value={invoice.letterOfCreditSourcePath ?? ""}
        disabled={disabled || isImporting}
        onChange={(value) => patchInvoice({ letterOfCreditSourcePath: value })}
        actions={
          <>
            {desktopAvailable ? (
              <DesktopIconButton title="选择信用证文件" disabled={disabled || isImporting} onClick={chooseLetterOfCreditFile}>
                <FileText size={15} aria-hidden="true" />
              </DesktopIconButton>
            ) : null}
            {renderOpenPathAction(invoice.letterOfCreditSourcePath, "打开信用证来源", showImportError)}
          </>
        }
      />
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
    </section>
  );
}
