import { ArrowDown, ArrowUp, Copy, Trash2 } from "lucide-react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportDesignerDocumentState } from "./reportDesignerHistory.ts";
import {
  duplicateSelectedBlock,
  moveSelectedBlock,
  removeSelectedBlock,
  updateSelectedBlock,
} from "./reportDesignerMutations.ts";
import type { ReportBlock } from "./reportDesignerSchema.ts";
import { blockLabel, findSelectedBlock, findSelectedSection, sectionLabel } from "./reportDesignerSelection.ts";
import { ConditionalBlockProperties } from "./ReportDesignerConditionalProperties.tsx";
import { DetailTableProperties } from "./ReportDesignerDetailTableProperties.tsx";
import { GridBlockProperties } from "./ReportDesignerGridProperties.tsx";
import { ImageBlockProperties } from "./ReportDesignerImageProperties.tsx";
import { PageProperties, SectionPrintProperties, SelectedSectionProperties } from "./ReportDesignerPageProperties.tsx";
import { BorderEditor, FieldPathInput, TextStyleEditor } from "./ReportDesignerPropertyControls.tsx";
import { RowBlockProperties } from "./ReportDesignerRowProperties.tsx";
import { updateSectionPrint } from "./reportDesignerPropertiesModel.ts";

export type ReportDesignerPropertiesPanelMode = "page" | "sectionPrint" | "component";

export function ReportDesignerPropertiesPanel({
  documentState,
  fieldGroups,
  mode,
  onCommit,
}: {
  documentState: ReportDesignerDocumentState;
  fieldGroups: ReportDesignerFieldGroup[];
  mode: ReportDesignerPropertiesPanelMode;
  onCommit: (nextState: ReportDesignerDocumentState) => void;
}) {
  const selected = findSelectedBlock(documentState.schema, documentState.selectedBlockId);
  const selectedSection = findSelectedSection(documentState.schema, documentState.selectedSectionId);
  const selectedIndex = selected?.section.blocks.findIndex((block) => block.id === selected.block.id) ?? -1;
  const canMoveSelectedUp = selectedIndex > 0;
  const canMoveSelectedDown = Boolean(selected && selectedIndex >= 0 && selectedIndex < selected.section.blocks.length - 1);

  return (
    <aside className="new-report-designer-properties">
      <section className="new-report-designer-panel">
        <div className="new-report-designer-panel-title">
          <span>{mode === "page" ? "页面" : mode === "sectionPrint" ? "版区打印" : selected ? "组件属性" : selectedSection ? "版区属性" : "组件属性"}</span>
        </div>
        {mode === "page" ? <PageProperties documentState={documentState} onCommit={onCommit} /> : null}
        {mode === "sectionPrint" ? <SectionPrintProperties documentState={documentState} onCommit={onCommit} /> : null}
        {mode === "component" ? (
          selected ? (
            <SelectedBlockProperties
              block={selected.block}
              fieldGroups={fieldGroups}
              onCommit={(block) => onCommit(updateSelectedBlock(documentState, () => block))}
              canMoveUp={canMoveSelectedUp}
              canMoveDown={canMoveSelectedDown}
              onMoveUp={() => onCommit(moveSelectedBlock(documentState, "up"))}
              onMoveDown={() => onCommit(moveSelectedBlock(documentState, "down"))}
              onDuplicate={() => onCommit(duplicateSelectedBlock(documentState))}
              onDelete={() => onCommit(removeSelectedBlock(documentState))}
            />
          ) : selectedSection ? (
            <SelectedSectionProperties
              section={selectedSection}
              onCommit={(patch) => onCommit(updateSectionPrint(documentState, selectedSection.id, patch))}
            />
          ) : (
            <div className="new-report-designer-muted">请选择画布上的组件或版区</div>
          )
        ) : null}
      </section>
    </aside>
  );
}

function SelectedBlockProperties({
  block,
  fieldGroups,
  onCommit,
  canMoveUp,
  canMoveDown,
  onMoveUp,
  onMoveDown,
  onDuplicate,
  onDelete,
}: {
  block: ReportBlock;
  fieldGroups: ReportDesignerFieldGroup[];
  onCommit: (block: ReportBlock) => void;
  canMoveUp: boolean;
  canMoveDown: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
  onDuplicate: () => void;
  onDelete: () => void;
}) {
  return (
    <div className="new-report-block-properties">
      <div className="new-report-property-readout">
        <span>类型</span>
        <strong>{blockLabel(block)}</strong>
      </div>
      <div className="new-report-block-action-row" aria-label="组件操作">
        <button className="icon-button compact-icon-button" type="button" title="上移" disabled={!canMoveUp} onClick={onMoveUp}>
          <ArrowUp size={16} aria-hidden="true" />
        </button>
        <button className="icon-button compact-icon-button" type="button" title="下移" disabled={!canMoveDown} onClick={onMoveDown}>
          <ArrowDown size={16} aria-hidden="true" />
        </button>
        <button className="icon-button compact-icon-button" type="button" title="复制" onClick={onDuplicate}>
          <Copy size={16} aria-hidden="true" />
        </button>
        <button className="icon-button compact-icon-button danger-icon" type="button" title="删除" onClick={onDelete}>
          <Trash2 size={16} aria-hidden="true" />
        </button>
      </div>
      <BlockOutputEditor
        block={block}
        onCommit={(patch) =>
          onCommit({
            ...block,
            output: patch,
          })
        }
      />
      {block.type === "Text" ? (
        <label className="new-report-property-wide">
          <span>内容</span>
          <textarea value={block.text} rows={3} onChange={(event) => onCommit({ ...block, text: event.target.value })} />
        </label>
      ) : null}
      {block.type === "Field" ? (
        <>
          <label>
            <span>标签</span>
            <input value={block.label ?? ""} onChange={(event) => onCommit({ ...block, label: event.target.value })} />
          </label>
          <FieldPathInput
            label="字段"
            value={block.fieldPath}
            fieldGroups={fieldGroups}
            onChange={(fieldPath) => onCommit({ ...block, fieldPath })}
          />
          <label>
            <span>占位文本</span>
            <input
              value={block.fallbackText ?? ""}
              onChange={(event) => onCommit({ ...block, fallbackText: event.target.value })}
            />
          </label>
        </>
      ) : null}
      {block.type === "Row" ? <RowBlockProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /> : null}
      {block.type === "Grid" ? <GridBlockProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /> : null}
      {block.type === "Conditional" ? <ConditionalBlockProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /> : null}
      {block.type === "Image" ? <ImageBlockProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /> : null}
      {block.type === "DetailTable" ? <DetailTableProperties block={block} fieldGroups={fieldGroups} onCommit={onCommit} /> : null}
      {"style" in block ? <TextStyleEditor style={block.style} onChange={(style) => onCommit({ ...block, style })} /> : null}
      {"border" in block ? <BorderEditor border={block.border} onChange={(border) => onCommit({ ...block, border })} /> : null}
    </div>
  );
}

function BlockOutputEditor({
  block,
  onCommit,
}: {
  block: ReportBlock;
  onCommit: (patch: NonNullable<ReportBlock["output"]>) => void;
}) {
  const output = {
    enabled: block.output?.enabled ?? true,
    note: block.output?.note ?? "",
  };

  return (
    <div className="new-report-output-editor">
      <label className="new-report-checkbox-label">
        <span>参与输出</span>
        <input
          type="checkbox"
          checked={output.enabled}
          onChange={(event) => onCommit({ enabled: event.target.checked, note: output.note })}
        />
      </label>
      <label className="new-report-property-wide">
        <span>设计备注</span>
        <textarea
          rows={2}
          value={output.note}
          placeholder="仅设计器可见，不写入打印内容"
          onChange={(event) => onCommit({ enabled: output.enabled, note: event.target.value })}
        />
      </label>
      {!output.enabled ? <div className="new-report-designer-muted">该组件会保留在设计器中，但保存后的 HTML/PDF 不输出。</div> : null}
    </div>
  );
}
