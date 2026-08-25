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
          <Code2 size={18} aria-hidden="true" />
          <span>报表设计</span>
          <small title={title}>当前模板：{title}</small>
        </div>
        <div className="toolbar-actions">
          <div className="segmented-control" role="tablist" aria-label="报表设计视图">
            <button
              className={workspaceMode === "design" && designerMode === "new" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={workspaceMode === "design" && designerMode === "new"}
              disabled={designDisabled}
              title={designDisabled ? "当前设备仅提供模板选择与预览，完整设计请使用桌面端" : undefined}
              onClick={() => onDesignerModeChange("new")}
            >
              <LayoutTemplate size={16} aria-hidden="true" />
              <span>可视化设计</span>
            </button>
            <button
              className={workspaceMode === "design" && designerMode === "source" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={workspaceMode === "design" && designerMode === "source"}
              disabled={designDisabled}
              title={designDisabled ? "高级 HTML 编辑请使用桌面端" : "适合熟悉 HTML 的高级用户"}
              onClick={() => onDesignerModeChange("source")}
            >
              <Code2 size={16} aria-hidden="true" />
              <span>高级 HTML</span>
            </button>
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
