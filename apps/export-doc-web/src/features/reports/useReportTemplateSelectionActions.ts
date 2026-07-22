import { Dispatch, SetStateAction } from "react";
import {
  buildNewTemplateFileName,
  readPreferredPreviewSampleProfile,
  type ReportTypeOption,
} from "./reportTemplateDesignerModel.ts";
import { readNumber } from "../../ui/formUtils.ts";
import { type ReportDesignerPreviewSampleProfile } from "../report-designer/reportDesignerPreviewSamples.ts";

export function useReportTemplateSelectionActions({
  reportType,
  setReportType,
  setSelectedUserTemplateId,
  setSelectedTemplatePath,
  setContent,
  setContentTemplatePath,
  setLoadedContent,
  setDesignerDraftContent,
  setNewTemplateFileName,
  setNewTemplateDisplayName,
  setNewUserTemplateName,
  setNewUserTemplateShareScope,
  setRenameTemplateFileName,
  setTemplatePreviewSampleProfile,
  setPreviewInvoiceId,
  setPreviewPaymentId,
  clearFeedback,
  confirmDiscardChanges,
}: {
  reportType: ReportTypeOption;
  setReportType: Dispatch<SetStateAction<ReportTypeOption>>;
  setSelectedUserTemplateId: Dispatch<SetStateAction<number>>;
  setSelectedTemplatePath: Dispatch<SetStateAction<string>>;
  setContent: Dispatch<SetStateAction<string>>;
  setContentTemplatePath: Dispatch<SetStateAction<string>>;
  setLoadedContent: Dispatch<SetStateAction<string>>;
  setDesignerDraftContent: Dispatch<SetStateAction<string>>;
  setNewTemplateFileName: Dispatch<SetStateAction<string>>;
  setNewTemplateDisplayName: Dispatch<SetStateAction<string>>;
  setNewUserTemplateName: Dispatch<SetStateAction<string>>;
  setNewUserTemplateShareScope: Dispatch<SetStateAction<string>>;
  setRenameTemplateFileName: Dispatch<SetStateAction<string>>;
  setTemplatePreviewSampleProfile: Dispatch<SetStateAction<ReportDesignerPreviewSampleProfile>>;
  setPreviewInvoiceId: Dispatch<SetStateAction<number>>;
  setPreviewPaymentId: Dispatch<SetStateAction<number>>;
  clearFeedback: () => void;
  confirmDiscardChanges: (actionLabel?: string) => Promise<boolean>;
}) {
  function clearLoadedTemplateContent() {
    setContent("");
    setContentTemplatePath("");
    setLoadedContent("");
    setDesignerDraftContent("");
    clearFeedback();
  }

  async function handleReportTypeChange(value: string) {
    const nextReportType = value === "PaymentVoucher" ? "PaymentVoucher" : "ExportDocument";
    if (nextReportType === reportType || !await confirmDiscardChanges("切换报表类型")) {
      return;
    }
    setReportType(nextReportType);
    setSelectedUserTemplateId(0);
    setSelectedTemplatePath("");
    clearLoadedTemplateContent();
    setNewTemplateFileName(buildNewTemplateFileName(nextReportType));
    setNewTemplateDisplayName("");
    setNewUserTemplateName("");
    setNewUserTemplateShareScope("Private");
    setRenameTemplateFileName("");
    setTemplatePreviewSampleProfile(readPreferredPreviewSampleProfile(nextReportType));
  }

  async function handleTemplateChange(value: string) {
    if (!await confirmDiscardChanges("切换默认模板")) {
      return;
    }
    setSelectedUserTemplateId(0);
    setSelectedTemplatePath(value);
    clearLoadedTemplateContent();
  }

  async function handleUserTemplateChange(value: string) {
    const id = readNumber(value);
    if (!await confirmDiscardChanges("切换我的或共享模板")) {
      return;
    }
    setSelectedUserTemplateId(id);
    if (id <= 0) {
      setSelectedTemplatePath("");
      clearLoadedTemplateContent();
    }
  }

  function handlePreviewSourceChange(value: string) {
    const nextValue = readNumber(value);
    if (reportType === "PaymentVoucher") {
      setPreviewPaymentId(nextValue);
    } else {
      setPreviewInvoiceId(nextValue);
    }
    clearFeedback();
  }

  return {
    clearLoadedTemplateContent,
    handleReportTypeChange,
    handleTemplateChange,
    handleUserTemplateChange,
    handlePreviewSourceChange,
  };
}
