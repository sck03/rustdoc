import { Dispatch, SetStateAction } from "react";
import {
  buildNewTemplateFileName,
  fileNameFromPath,
  readPreferredPreviewSampleProfile,
  type ReportTypeOption,
} from "./reportTemplateDesignerModel.ts";
import { readNumber } from "../../ui/formUtils.ts";
import { type ReportDesignerPreviewSampleProfile } from "../report-designer/reportDesignerPreviewSamples.ts";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";

export function useReportTemplateSelectionActions({
  reportType,
  setReportType,
  setSelectedUserTemplateId,
  setSelectedTemplatePath,
  setNewTemplateFileName,
  setNewTemplateDisplayName,
  setNewUserTemplateName,
  setRenameTemplateFileName,
  setTemplatePreviewSampleProfile,
  setPreviewInvoiceId,
  setPreviewPaymentId,
  clearFeedback,
  clearLoadedTemplateContent,
  confirmDiscardChanges,
}: {
  reportType: ReportTypeOption;
  setReportType: Dispatch<SetStateAction<ReportTypeOption>>;
  setSelectedUserTemplateId: Dispatch<SetStateAction<number>>;
  setSelectedTemplatePath: Dispatch<SetStateAction<string>>;
  setNewTemplateFileName: Dispatch<SetStateAction<string>>;
  setNewTemplateDisplayName: Dispatch<SetStateAction<string>>;
  setNewUserTemplateName: Dispatch<SetStateAction<string>>;
  setRenameTemplateFileName: Dispatch<SetStateAction<string>>;
  setTemplatePreviewSampleProfile: Dispatch<SetStateAction<ReportDesignerPreviewSampleProfile>>;
  setPreviewInvoiceId: Dispatch<SetStateAction<number>>;
  setPreviewPaymentId: Dispatch<SetStateAction<number>>;
  clearFeedback: () => void;
  clearLoadedTemplateContent: () => void;
  confirmDiscardChanges: (actionLabel?: string) => Promise<boolean>;
}) {
  const { update } = useRouteQuery();
  async function handleReportTypeChange(value: string) {
    const nextReportType = value === "PaymentVoucher" ? "PaymentVoucher" : "ExportDocument";
    if (nextReportType === reportType || !await confirmDiscardChanges("切换报表类型")) {
      return;
    }
    setReportType(nextReportType);
    update({ reportType: nextReportType, template: null, userTemplateId: null });
    setSelectedUserTemplateId(0);
    setSelectedTemplatePath("");
    clearLoadedTemplateContent();
    setNewTemplateFileName(buildNewTemplateFileName(nextReportType));
    setNewTemplateDisplayName("");
    setNewUserTemplateName("");
    setRenameTemplateFileName("");
    setTemplatePreviewSampleProfile(readPreferredPreviewSampleProfile(nextReportType));
  }

  async function handleTemplateChange(value: string) {
    if (!await confirmDiscardChanges("切换默认模板")) {
      return;
    }
    setSelectedUserTemplateId(0);
    setSelectedTemplatePath(value);
    update({ reportType, template: fileNameFromPath(value), userTemplateId: null });
    clearLoadedTemplateContent();
  }

  async function handleUserTemplateChange(value: string) {
    const id = readNumber(value);
    if (!await confirmDiscardChanges("切换我的或共享模板")) {
      return;
    }
    setSelectedUserTemplateId(id);
    update({ reportType, userTemplateId: id || null, template: null });
    setSelectedTemplatePath("");
    clearLoadedTemplateContent();
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
