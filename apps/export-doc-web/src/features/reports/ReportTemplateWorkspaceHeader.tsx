import { ArrowLeft, Code2, Eye, LayoutTemplate, Save } from "lucide-react";
import { type DesignerMode, type TemplateWorkspaceMode } from "./reportTemplateDesignerModel.ts";
import { Button } from "../../ui/Button.tsx";

export function ReportTemplateWorkspaceHeader({
  title,
  designerMode,
  workspaceMode,
  canPreview,
  canSave,
  designDisabled,
  v3Disabled,
  onBackToManagement,
  onDesignerModeChange,
  onPreview,
}: {
  title: string;
  designerMode: DesignerMode;
  workspaceMode: TemplateWorkspaceMode;
  canPreview: boolean;
  canSave: boolean;
  designDisabled?: boolean;
  v3Disabled?: boolean;
  onBackToManagement: () => void;
  onDesignerModeChange: (mode: DesignerMode) => void;
  onPreview: () => void;
}) {
  return (
    <div className="report-template-sticky-header">
      <div className="editor-toolbar report-template-designer-toolbar">
        <Button variant="secondary" icon={<ArrowLeft size={17} aria-hidden="true" />} onClick={onBackToManagement}>
          返回模板管理
        </Button>
        <div className="editor-title report-template-current-title">
          <LayoutTemplate size={18} aria-hidden="true" />
          <span>报表设计</span>
          <small title={title}>当前模板：{title}</small>
        </div>
        <div className="toolbar-actions">
          <div
            className={`segmented-control report-template-view-tabs ${designerMode === "advancedHtml" ? "has-advanced-html" : "v3-only"}`}
            role="tablist"
            aria-label="报表设计视图"
          >
            <button
              className={workspaceMode === "design" && designerMode === "v3" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={workspaceMode === "design" && designerMode === "v3"}
              disabled={designDisabled || v3Disabled}
              title={designDisabled ? "当前设备仅提供模板选择与预览，完整设计请使用桌面端" : v3Disabled ? "高级 HTML 模板保持独立运行，请使用高级 HTML 编辑" : undefined}
              onClick={() => onDesignerModeChange("v3")}
            >
              <LayoutTemplate size={16} aria-hidden="true" />
              <span>可视化设计</span>
            </button>
            {designerMode === "advancedHtml" ? (
              <button
                className={workspaceMode === "design" ? "segmented-active" : ""}
                type="button"
                role="tab"
                aria-selected={workspaceMode === "design"}
                disabled={designDisabled}
                title={designDisabled ? "高级 HTML 编辑请使用桌面端" : "适合复杂表格、合并单元格和精确分页"}
                onClick={() => onDesignerModeChange("advancedHtml")}
              >
                <Code2 size={16} aria-hidden="true" />
                <span>高级 HTML</span>
              </button>
            ) : null}
            <button
              className={workspaceMode === "preview" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={workspaceMode === "preview"}
              disabled={!canPreview}
              onClick={onPreview}
            >
              <Eye size={16} aria-hidden="true" />
              <span>预览</span>
            </button>
          </div>
          <Button variant="primary" type="submit" icon={<Save size={17} aria-hidden="true" />} disabled={designDisabled || !canSave}>保存</Button>
        </div>
      </div>
    </div>
  );
}
