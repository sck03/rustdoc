import type { Dispatch, SetStateAction } from "react";
import type { ApiReportTemplatePreviewResponse } from "../../api/index.ts";
import { hasValidReportDesignerV3Schema } from "../report-designer/reportDesignerV3TemplateParser.ts";
import type { ReportDesignerPreviewSampleProfile } from "../report-designer/reportDesignerPreviewSamples.ts";
import {
  normalizePreviewSampleProfile,
  type ReportTypeOption,
  type TemplatePreviewMode,
  type TemplateWorkspaceMode,
} from "./reportTemplateDesignerModel.ts";

type MessageType = "success" | "error" | null;

export function useReportTemplateEditingActions({
  canDesignTemplates,
  canManageTemplates,
  canRenderTemplatePreview,
  content,
  currentUserTemplateCanEdit,
  isLocalSamplePreview,
  isUserTemplate,
  reportType,
  selectedTemplateContentActive,
  selectedTemplatePath,
  templatePreviewMode,
  renderInvoicePreview,
  renderPaymentPreview,
  renderSamplePreview,
  saveDefaultTemplateContent,
  saveUserTemplateContent,
  setMessage,
  setMessageType,
  setPreview,
  setTemplatePreviewMode,
  setTemplatePreviewSampleProfile,
  setWorkspaceMode,
}: {
  canDesignTemplates: boolean;
  canManageTemplates: boolean;
  canRenderTemplatePreview: boolean;
  content: string;
  currentUserTemplateCanEdit: boolean;
  isLocalSamplePreview: boolean;
  isUserTemplate: boolean;
  reportType: ReportTypeOption;
  selectedTemplateContentActive: boolean;
  selectedTemplatePath: string;
  templatePreviewMode: TemplatePreviewMode;
  renderInvoicePreview: () => void;
  renderPaymentPreview: () => void;
  renderSamplePreview: () => void;
  saveDefaultTemplateContent: (content: string) => void;
  saveUserTemplateContent: (content: string) => void;
  setMessage: Dispatch<SetStateAction<string | null>>;
  setMessageType: Dispatch<SetStateAction<MessageType>>;
  setPreview: Dispatch<SetStateAction<ApiReportTemplatePreviewResponse | null>>;
  setTemplatePreviewMode: Dispatch<SetStateAction<TemplatePreviewMode>>;
  setTemplatePreviewSampleProfile: Dispatch<SetStateAction<ReportDesignerPreviewSampleProfile>>;
  setWorkspaceMode: Dispatch<SetStateAction<TemplateWorkspaceMode>>;
}) {
  async function confirmStructuredTemplateOverwrite() {
    if (hasValidReportDesignerV3Schema(content)) {
      return true;
    }
    setMessage("模板格式无效，不能保存。请重新加载或新建 .dtpl 模板。");
    setMessageType("error");
    return false;
  }

  async function handleSaveNewReportDesignerContent(nextContent: string) {
    if (!selectedTemplatePath || !selectedTemplateContentActive) {
      return;
    }
    if (isUserTemplate ? !canDesignTemplates || !currentUserTemplateCanEdit : !canManageTemplates) {
      setMessage("当前账号没有保存模板权限。");
      setMessageType("error");
      return;
    }
    if (!await confirmStructuredTemplateOverwrite()) {
      return;
    }

    setPreview(null);
    setMessage(null);
    setMessageType(null);
    if (isUserTemplate) {
      saveUserTemplateContent(nextContent);
    } else {
      saveDefaultTemplateContent(nextContent);
    }
  }

  function handleTemplatePreviewModeChange(nextMode: TemplatePreviewMode) {
    setTemplatePreviewMode(nextMode);
    setWorkspaceMode("preview");
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }

  function handleTemplatePreviewSampleProfileChange(value: string) {
    setTemplatePreviewSampleProfile(normalizePreviewSampleProfile(value, reportType));
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }

  function handleRenderTemplatePreview() {
    if (!canRenderTemplatePreview) {
      return;
    }

    setWorkspaceMode("preview");
    if (templatePreviewMode === "sample") {
      if (isLocalSamplePreview) {
        setPreview(null);
        setMessage(null);
        setMessageType(null);
        return;
      }
      renderSamplePreview();
      return;
    }

    if (reportType === "PaymentVoucher") {
      renderPaymentPreview();
    } else {
      renderInvoicePreview();
    }
  }

  return {
    handleRenderTemplatePreview,
    handleSaveNewReportDesignerContent,
    handleTemplatePreviewModeChange,
    handleTemplatePreviewSampleProfileChange,
  };
}
