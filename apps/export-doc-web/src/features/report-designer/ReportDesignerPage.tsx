import { type CSSProperties, useEffect, useMemo, useState } from "react";
import type { ApiReportTemplateFieldCatalogResponse } from "../../api/index.ts";
import { createDefaultReportDesignerSchema } from "./reportDesignerDefaults.ts";
import { buildReportDesignerFieldGroups } from "./reportDesignerFields.ts";
import { useReportDesignerHistory } from "./reportDesignerHistory.ts";
import { exportReportDesignerSchemaToHtml } from "./reportDesignerHtmlExporter.ts";
import { ReportDesignerCanvas } from "./ReportDesignerCanvas.tsx";
import { ReportDesignerPropertiesPanel, type ReportDesignerPropertiesPanelMode } from "./ReportDesignerPropertiesPanel.tsx";
import { ReportDesignerSidebar } from "./ReportDesignerSidebar.tsx";
import { ReportDesignerToolbar } from "./ReportDesignerToolbar.tsx";
import {
  createConditionalBlock,
  createDetailTableBlock,
  createFieldBlock,
  createGridBlock,
  createImageBlock,
  createPageBreakBlock,
  createRowBlock,
  createTextBlock,
  duplicateSelectedBlock,
  insertBlockAtDropTarget,
  insertBlockAfterSelection,
  moveBlockToDropTarget,
  moveSelectedBlock,
  removeSelectedBlock,
  type ReportDesignerBlockDropTarget,
} from "./reportDesignerMutations.ts";
import type { ReportDesignerDragPayload, ReportDesignerPaletteComponentType } from "./reportDesignerDragDrop.ts";
import { hasReportDesignerSchema, parseReportDesignerSchemaFromHtml } from "./reportDesignerTemplateParser.ts";
import type { ReportDesignerReportType, ReportDesignerSchema } from "./reportDesignerSchema.ts";
import {
  hasBlockingReportDesignerSchemaIssues,
  validateReportDesignerSchema,
} from "./reportDesignerSchemaValidation.ts";

type ReportDesignerRightRailMode = ReportDesignerPropertiesPanelMode;

export function ReportDesignerPage({
  reportType,
  displayName,
  content,
  fieldCatalog,
  canApplyTemplateContent,
  canSaveTemplateContent,
  hasTemplateChanges,
  onApplyTemplateContent,
  onSaveTemplateContent,
  onDesignerDraftContentChange,
  onOpenSource,
}: {
  reportType: ReportDesignerReportType;
  displayName: string;
  content: string;
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse | null;
  canApplyTemplateContent: boolean;
  canSaveTemplateContent: boolean;
  hasTemplateChanges: boolean;
  onApplyTemplateContent: (nextContent: string) => void;
  onSaveTemplateContent: (nextContent: string) => void;
  onDesignerDraftContentChange?: (nextContent: string) => void;
  onOpenSource: () => void;
}) {
  const [rightRailMode, setRightRailMode] = useState<ReportDesignerRightRailMode>("component");
  const initialSchema = useMemo(
    () => parseReportDesignerSchemaFromHtml(content) ?? createDefaultReportDesignerSchema(reportType),
    [content, reportType],
  );
  const fieldGroups = useMemo(() => buildReportDesignerFieldGroups(fieldCatalog, reportType), [fieldCatalog, reportType]);
  const history = useReportDesignerHistory(initialSchema);
  const exportedHtml = useMemo(() => exportReportDesignerSchemaToHtml(history.state.schema), [history.state.schema]);
  const designerPaperHeightMm = useMemo(() => readDesignerPaperHeightMm(history.state.schema), [history.state.schema]);
  const schemaIssues = useMemo(() => validateReportDesignerSchema(history.state.schema), [history.state.schema]);
  const hasBlockingSchemaIssues = hasBlockingReportDesignerSchemaIssues(schemaIssues);
  const existingContentWithoutSchema = Boolean(content.trim()) && !hasReportDesignerSchema(content);
  const hasUnappliedDesignerChanges = exportedHtml !== content;

  useEffect(() => {
    onDesignerDraftContentChange?.(exportedHtml);
  }, [exportedHtml, onDesignerDraftContentChange]);

  function applyExportedTemplateContent() {
    onApplyTemplateContent(exportedHtml);
  }

  function saveExportedTemplateContent() {
    onSaveTemplateContent(exportedHtml);
  }

  function resetToDefaultSchema() {
    history.commitState({
      schema: createDefaultReportDesignerSchema(reportType),
      selectedBlockId: null,
      selectedSectionId: null,
    });
  }

  function insertTextBlock() {
    history.commitState(insertBlockAfterSelection(history.state, createTextBlock()));
  }

  function insertRowBlock() {
    history.commitState(insertBlockAfterSelection(history.state, createRowBlock(reportType)));
  }

  function insertGridBlock() {
    history.commitState(insertBlockAfterSelection(history.state, createGridBlock(reportType)));
  }

  function insertConditionalBlock() {
    history.commitState(insertBlockAfterSelection(history.state, createConditionalBlock(reportType)));
  }

  function insertImageBlock() {
    history.commitState(insertBlockAfterSelection(history.state, createImageBlock()));
  }

  function insertDetailTableBlock() {
    if (reportType !== "ExportDocument") {
      return;
    }

    history.commitState(insertBlockAfterSelection(history.state, createDetailTableBlock()));
  }

  function insertPageBreakBlock() {
    history.commitState(insertBlockAfterSelection(history.state, createPageBreakBlock()));
  }

  function insertFieldBlock(field: { label: string; value: string }) {
    history.commitState(insertBlockAfterSelection(history.state, createFieldBlock(field.label, field.value)));
  }

  function createBlockFromPalette(componentType: ReportDesignerPaletteComponentType) {
    switch (componentType) {
      case "Text":
        return createTextBlock();
      case "Row":
        return createRowBlock(reportType);
      case "Grid":
        return createGridBlock(reportType);
      case "Conditional":
        return createConditionalBlock(reportType);
      case "Image":
        return createImageBlock();
      case "DetailTable":
        return reportType === "ExportDocument" ? createDetailTableBlock() : null;
      case "PageBreak":
        return createPageBreakBlock();
    }
  }

  function dropDesignerItem(payload: ReportDesignerDragPayload, target: ReportDesignerBlockDropTarget) {
    if (payload.kind === "Block") {
      history.commitState(moveBlockToDropTarget(history.state, payload.blockId, target));
      return;
    }

    if (payload.kind === "Field") {
      history.commitState(insertBlockAtDropTarget(history.state, createFieldBlock(payload.label, payload.value), target));
      return;
    }

    const block = createBlockFromPalette(payload.componentType);
    if (block) {
      history.commitState(insertBlockAtDropTarget(history.state, block, target));
    }
  }

  function selectBlock(blockId: string) {
    history.selectBlock(blockId);
    setRightRailMode("component");
  }

  function selectSection(sectionId: string) {
    history.selectSection(sectionId);
    setRightRailMode("component");
  }

  useEffect(() => {
    function handleDesignerKeyDown(event: KeyboardEvent) {
      if (isEditableKeyboardTarget(event.target)) {
        return;
      }

      if ((event.key === "Delete" || event.key === "Backspace") && history.state.selectedBlockId) {
        event.preventDefault();
        history.commitState(removeSelectedBlock(history.state));
        return;
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
        event.preventDefault();
        if (event.shiftKey) {
          history.redo();
        } else {
          history.undo();
        }
        return;
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "y") {
        event.preventDefault();
        history.redo();
        return;
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "d" && history.state.selectedBlockId) {
        event.preventDefault();
        history.commitState(duplicateSelectedBlock(history.state));
        return;
      }

      if (event.altKey && history.state.selectedBlockId && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
        event.preventDefault();
        history.commitState(moveSelectedBlock(history.state, event.key === "ArrowUp" ? "up" : "down"));
      }
    }

    window.addEventListener("keydown", handleDesignerKeyDown);
    return () => window.removeEventListener("keydown", handleDesignerKeyDown);
  }, [history]);

  return (
    <div className="new-report-designer">
      <ReportDesignerToolbar
        canUndo={history.canUndo}
        canRedo={history.canRedo}
        canApply={canApplyTemplateContent && hasUnappliedDesignerChanges && !hasBlockingSchemaIssues}
        canSave={canSaveTemplateContent && (hasUnappliedDesignerChanges || hasTemplateChanges) && !hasBlockingSchemaIssues}
        onBack={onOpenSource}
        onReset={resetToDefaultSchema}
        onApply={applyExportedTemplateContent}
        onSave={saveExportedTemplateContent}
        onInsertText={insertTextBlock}
        onInsertRow={insertRowBlock}
        onInsertGrid={insertGridBlock}
        onInsertConditional={insertConditionalBlock}
        onInsertImage={insertImageBlock}
        onInsertDetailTable={insertDetailTableBlock}
        onInsertPageBreak={insertPageBreakBlock}
        canInsertDetailTable={reportType === "ExportDocument"}
        onUndo={history.undo}
        onRedo={history.redo}
      />
      <div className="new-report-designer-header">
        <div>
          <span>新版设计器</span>
          <h2>{displayName || "报表模板"}</h2>
        </div>
      </div>
      {existingContentWithoutSchema ? (
        <div className="new-report-designer-warning">
          当前模板未包含新版设计器结构，实验入口已打开默认结构草稿。应用或保存前会再次确认，不会静默转换现有 HTML。
        </div>
      ) : null}
      {schemaIssues.length > 0 ? (
        <div className={hasBlockingSchemaIssues ? "new-report-designer-error" : "new-report-designer-warning"}>
          <strong>{hasBlockingSchemaIssues ? "结构校验未通过，修正后才能应用或保存。" : "结构已按兼容规则自动整理。"}</strong>
          {schemaIssues.slice(0, 4).map((issue) => (
            <div key={`${issue.severity}-${issue.path}-${issue.message}`}>
              {issue.path}: {issue.message}
            </div>
          ))}
          {schemaIssues.length > 4 ? <div>还有 {schemaIssues.length - 4} 项校验提示。</div> : null}
        </div>
      ) : null}
      <div
        className="new-report-designer-grid"
        style={{ "--new-report-paper-height": `${designerPaperHeightMm}mm` } as CSSProperties}
      >
        <ReportDesignerSidebar
          schema={history.state.schema}
          selectedBlockId={history.state.selectedBlockId}
          selectedSectionId={history.state.selectedSectionId}
          fieldGroups={fieldGroups}
          canInsertDetailTable={reportType === "ExportDocument"}
          onSelectBlock={selectBlock}
          onSelectSection={selectSection}
          onInsertText={insertTextBlock}
          onInsertRow={insertRowBlock}
          onInsertGrid={insertGridBlock}
          onInsertConditional={insertConditionalBlock}
          onInsertImage={insertImageBlock}
          onInsertDetailTable={insertDetailTableBlock}
          onInsertPageBreak={insertPageBreakBlock}
          onInsertField={insertFieldBlock}
          onDropDesignerItem={dropDesignerItem}
        />
        <ReportDesignerCanvas
          schema={history.state.schema}
          selectedBlockId={history.state.selectedBlockId}
          selectedSectionId={history.state.selectedSectionId}
          onSelectBlock={selectBlock}
          onSelectSection={selectSection}
          onDropDesignerItem={dropDesignerItem}
        />
        <div className="new-report-designer-right-rail">
          <div className="new-report-designer-rail-tabs" role="tablist" aria-label="设计器右侧面板">
            <button
              className={rightRailMode === "page" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={rightRailMode === "page"}
              onClick={() => setRightRailMode("page")}
            >
              页面
            </button>
            <button
              className={rightRailMode === "sectionPrint" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={rightRailMode === "sectionPrint"}
              onClick={() => setRightRailMode("sectionPrint")}
            >
              版区打印
            </button>
            <button
              className={rightRailMode === "component" ? "segmented-active" : ""}
              type="button"
              role="tab"
              aria-selected={rightRailMode === "component"}
              onClick={() => setRightRailMode("component")}
            >
              组件属性
            </button>
          </div>
          <ReportDesignerPropertiesPanel
            documentState={history.state}
            fieldGroups={fieldGroups}
            mode={rightRailMode}
            onCommit={history.commitState}
          />
        </div>
      </div>
    </div>
  );
}

function readDesignerPaperHeightMm(schema: ReportDesignerSchema) {
  const portrait = schema.page.size === "A5"
    ? { widthMm: 148, heightMm: 210 }
    : schema.page.size === "Letter"
      ? { widthMm: 216, heightMm: 279 }
      : schema.page.size === "Custom"
        ? { widthMm: schema.page.widthMm ?? 210, heightMm: schema.page.heightMm ?? 297 }
        : { widthMm: 210, heightMm: 297 };

  return schema.page.orientation === "Landscape" ? portrait.widthMm : portrait.heightMm;
}

function isEditableKeyboardTarget(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  const tagName = target.tagName.toLowerCase();
  return tagName === "input" ||
    tagName === "textarea" ||
    tagName === "select" ||
    target.isContentEditable;
}
