import type { Dispatch, SetStateAction } from "react";
import type { ApiReportTemplatePreviewResponse } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { hasValidReportDesignerV3Schema } from "../report-designer/reportDesignerV3TemplateParser.ts";
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
    if (!content.trim() || hasValidReportDesignerV3Schema(content)) {
      return true;
    }

    return requestConfirmation({
      title: "转换为 V3 可视化模板",
      description: "当前模板使用高级 HTML 运行时，适合复杂表格、合并单元格和精确分页。继续后会在内存中创建新的 A4 V3 草稿，原 HTML 只有在明确保存时才会被替换。",
      details: ["如需保持原版式，请继续使用高级 HTML。", "建议在转换前导出模板包备份。"],
      confirmLabel: "确认启用",
    });
  }

  function handleDesignerModeChange(mode: DesignerMode) {
    if (isLimitedReportView || mode === designerMode || (mode === "v3" && designerMode === "advancedHtml")) {
      return;
    }
    if (designerMode === "v3" && workspaceHasUnappliedDesignerChanges) {
      setContent(designerDraftContent);
      setContentTemplatePath(selectedTemplatePath);
      setPreview(null);
    }
    setWorkspaceMode("design");
    setDesignerMode(mode);
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
    handleDesignerModeChange,
    handleFormatSource,
    handleRenderTemplatePreview,
    handleSaveNewReportDesignerContent,
    handleTemplatePreviewModeChange,
    handleTemplatePreviewSampleProfileChange,
  };
}
