import { Code2, Eye, LayoutTemplate, RefreshCw, Save } from "lucide-react";
import { type DesignerMode, type TemplateWorkspaceMode } from "./reportTemplateDesignerModel.ts";
import { Button, IconButton } from "../../ui/Button.tsx";

export function ReportTemplateWorkspaceHeader({
  title,
  designerMode,
  workspaceMode,
  isBusy,
  canPreview,
  canSave,
  onDesignerModeChange,
  onWorkspaceModeChange,
  onRefresh,
  onPreview,
}: {
  title: string;
  designerMode: DesignerMode;
  workspaceMode: TemplateWorkspaceMode;
  isBusy: boolean;
  canPreview: boolean;
  canSave: boolean;
  onDesignerModeChange: (mode: DesignerMode) => void;
  onWorkspaceModeChange: (mode: TemplateWorkspaceMode) => void;
  onRefresh: () => void;
  onPreview: () => void;
}) {
  return (
    <div className="report-template-sticky-header">
      <div className="editor-toolbar">
        <div className="editor-title">
          <Code2 size={18} aria-hidden="true" />
          <span>{title}</span>
        </div>
        <div className="toolbar-actions">
          <div className="segmented-control" role="tablist" aria-label="模板设计模式">
            <button
              className={designerMode === "new" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={designerMode === "new"}
              onClick={() => onDesignerModeChange("new")}
            >
              <LayoutTemplate size={16} aria-hidden="true" />
              <span>新版设计器</span>
            </button>
            <button
              className={designerMode === "source" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={designerMode === "source"}
              onClick={() => onDesignerModeChange("source")}
            >
              <Code2 size={16} aria-hidden="true" />
              <span>源码</span>
            </button>
          </div>
          <IconButton label="刷新报表模板" disabled={isBusy} onClick={onRefresh}>
            <RefreshCw size={18} aria-hidden="true" />
          </IconButton>
          <Button variant="secondary" icon={<Eye size={17} aria-hidden="true" />} disabled={!canPreview} onClick={onPreview}>预览</Button>
          <Button variant="primary" type="submit" icon={<Save size={17} aria-hidden="true" />} disabled={!canSave}>保存</Button>
        </div>
      </div>

      <div className="report-template-workspace-tabs" role="tablist" aria-label="模板工作区">
        <button
          className={workspaceMode === "design" ? "segmented-active" : ""}
          type="button"
          role="tab"
          aria-selected={workspaceMode === "design"}
          onClick={() => onWorkspaceModeChange("design")}
        >
          <LayoutTemplate size={16} aria-hidden="true" />
          <span>设计</span>
        </button>
        <button
          className={workspaceMode === "preview" ? "segmented-active" : ""}
          type="button"
          role="tab"
          aria-selected={workspaceMode === "preview"}
          onClick={() => onWorkspaceModeChange("preview")}
        >
          <Eye size={16} aria-hidden="true" />
          <span>预览</span>
        </button>
      </div>
    </div>
  );
}
