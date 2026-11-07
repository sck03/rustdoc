import { ArrowLeft, Eye, LayoutTemplate, Save } from "lucide-react";
import { type TemplateWorkspaceMode } from "./reportTemplateDesignerModel.ts";
import { Button } from "../../ui/Button.tsx";
import { SegmentedNav } from "../../ui/primitives/Tabs.tsx";

export function ReportTemplateWorkspaceHeader({
  title,
  workspaceMode,
  canPreview,
  canSave,
  onBackToManagement,
  onDesign,
  onPreview,
}: {
  title: string;
  workspaceMode: TemplateWorkspaceMode;
  canPreview: boolean;
  canSave: boolean;
  onBackToManagement: () => void;
  onDesign: () => void;
  onPreview: () => void;
}) {
  return (
    <div className="report-template-sticky-header">
      <div className="editor-toolbar report-template-designer-toolbar">
        <Button variant="secondary" icon={<ArrowLeft size={18} aria-hidden="true" />} onClick={onBackToManagement}>
          返回模板管理
        </Button>
        <div className="editor-title report-template-current-title">
          <LayoutTemplate size={18} aria-hidden="true" />
          <span>报表设计</span>
          <small title={title}>当前模板：{title}</small>
        </div>
        <div className="toolbar-actions">
          <div
            className="report-template-view-tabs"
          >
            <SegmentedNav label="报表设计视图" value={workspaceMode} onChange={value => value === "design" ? onDesign() : onPreview()}
              items={[
                { key: "design", label: <><LayoutTemplate size={16} aria-hidden="true" /><span>可视化设计</span></> },
                { key: "preview", disabled: !canPreview, label: <><Eye size={16} aria-hidden="true" /><span>预览</span></> },
              ]} />
          </div>
          <Button variant="primary" type="submit" icon={<Save size={18} aria-hidden="true" />} disabled={!canSave}>保存</Button>
        </div>
      </div>
    </div>
  );
}
