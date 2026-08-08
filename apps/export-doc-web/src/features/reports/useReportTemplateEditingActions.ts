import type { Dispatch, SetStateAction } from "react";
import type { ApiReportTemplatePreviewResponse } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { hasReportDesignerSchema } from "../report-designer/reportDesignerTemplateParser.ts";
import type { ReportDesignerPreviewSampleProfile } from "../report-designer/reportDesignerPreviewSamples.ts";
import { formatReportTemplateSource } from "./reportTemplateFormatter.ts";
import {
  normalizePreviewSampleProfile,
  type DesignerMode,
  type ReportTypeOption,
  type TemplatePreviewMode,
  type TemplateWorkspaceMode,
} from "./reportTemplateDesignerModel.ts";

type MessageType = "success" | "error" | null;

export function useReportTemplateEditingActions({
  canDesignTemplates,
  canFormatSource,
  canManageTemplates,
  canRenderTemplatePreview,
  content,
  currentUserTemplateCanEdit,
  designerDraftContent,
  designerMode,
  isLimitedReportView,
  isLocalSamplePreview,
  isUserTemplate,
  reportType,
  selectedTemplateContentActive,
  selectedTemplatePath,
  templatePreviewMode,
  workspaceHasUnappliedDesignerChanges,
  renderInvoicePreview,
  renderPaymentPreview,
  renderSamplePreview,
  saveDefaultTemplateContent,
  saveUserTemplateContent,
  setContent,
  setContentTemplatePath,
  setDesignerMode,
  setMessage,
  setMessageType,
  setPreview,
  setTemplatePreviewMode,
  setTemplatePreviewSampleProfile,
  setWorkspaceMode,
}: {
  canDesignTemplates: boolean;
  canFormatSource: boolean;
  canManageTemplates: boolean;
  canRenderTemplatePreview: boolean;
  content: string;
  currentUserTemplateCanEdit: boolean;
  designerDraftContent: string;
  designerMode: DesignerMode;
  isLimitedReportView: boolean;
  isLocalSamplePreview: boolean;
  isUserTemplate: boolean;
  reportType: ReportTypeOption;
  selectedTemplateContentActive: boolean;
  selectedTemplatePath: string;
  templatePreviewMode: TemplatePreviewMode;
  workspaceHasUnappliedDesignerChanges: boolean;
  renderInvoicePreview: () => void;
  renderPaymentPreview: () => void;
  renderSamplePreview: () => void;
  saveDefaultTemplateContent: (content: string) => void;
  saveUserTemplateContent: (content: string) => void;
  setContent: Dispatch<SetStateAction<string>>;
  setContentTemplatePath: Dispatch<SetStateAction<string>>;
  setDesignerMode: Dispatch<SetStateAction<DesignerMode>>;
  setMessage: Dispatch<SetStateAction<string | null>>;
  setMessageType: Dispatch<SetStateAction<MessageType>>;
  setPreview: Dispatch<SetStateAction<ApiReportTemplatePreviewResponse | null>>;
  setTemplatePreviewMode: Dispatch<SetStateAction<TemplatePreviewMode>>;
  setTemplatePreviewSampleProfile: Dispatch<SetStateAction<ReportDesignerPreviewSampleProfile>>;
  setWorkspaceMode: Dispatch<SetStateAction<TemplateWorkspaceMode>>;
}) {
  const requestConfirmation = useConfirmation();

  async function confirmStructuredTemplateOverwrite() {
    if (!content.trim() || hasReportDesignerSchema(content)) {
      return true;
    }

    return requestConfirmation({
      title: "启用可视化设计结构",
      description: "当前模板尚未包含可视化设计结构。继续后将使用当前布局覆盖原有高级 HTML。",
      details: ["建议在转换前导出模板包备份。"],
      confirmLabel: "确认启用",
    });
  }

  function handleDesignerModeChange(mode: DesignerMode) {
    if (isLimitedReportView || mode === designerMode) {
      return;
    }
    if (designerMode === "new" && workspaceHasUnappliedDesignerChanges) {
      setContent(designerDraftContent);
      setContentTemplatePath(selectedTemplatePath);
      setPreview(null);
    }
    setWorkspaceMode("design");
    setDesignerMode(mode);
  }

  async function handleApplyNewReportDesignerContent(nextContent: string) {
    if (!selectedTemplatePath || !selectedTemplateContentActive || (isUserTemplate && !currentUserTemplateCanEdit)) {
      return;
    }
    if (!await confirmStructuredTemplateOverwrite()) {
      return;
    }

    setContent(nextContent);
    setContentTemplatePath(selectedTemplatePath);
    setPreview(null);
    setMessage("可视化设计内容已应用到模板，保存后写入模板文件。");
    setMessageType("success");
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

    setContent(nextContent);
    setContentTemplatePath(selectedTemplatePath);
    setPreview(null);
    setMessage(null);
    setMessageType(null);
    if (isUserTemplate) {
      saveUserTemplateContent(nextContent);
    } else {
      saveDefaultTemplateContent(nextContent);
    }
  }

  function handleFormatSource() {
    if (!canFormatSource) {
      return;
    }

    setContent(formatReportTemplateSource(content));
    setContentTemplatePath(selectedTemplatePath);
    setPreview(null);
    setMessage("高级 HTML 已格式化，保存后写入模板文件。");
    setMessageType("success");
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
    handleApplyNewReportDesignerContent,
    handleDesignerModeChange,
    handleFormatSource,
    handleRenderTemplatePreview,
    handleSaveNewReportDesignerContent,
    handleTemplatePreviewModeChange,
    handleTemplatePreviewSampleProfileChange,
  };
}
