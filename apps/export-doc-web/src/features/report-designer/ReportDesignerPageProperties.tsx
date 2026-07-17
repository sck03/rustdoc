import type { ReportDesignerDocumentState } from "./reportDesignerHistory.ts";
import type { ReportSection } from "./reportDesignerSchema.ts";
import { normalizeNumber, normalizePageSize, updatePage, updateSectionPrint } from "./reportDesignerPropertiesModel.ts";
import { blockLabel, sectionLabel } from "./reportDesignerSelection.ts";

export function PageProperties({
  documentState,
  onCommit,
}: {
  documentState: ReportDesignerDocumentState;
  onCommit: (nextState: ReportDesignerDocumentState) => void;
}) {
  return (
    <div className="new-report-property-grid">
      <label>
        <span>纸张</span>
        <select
          value={documentState.schema.page.size}
          onChange={(event) => onCommit(updatePage(documentState, { size: normalizePageSize(event.target.value) }))}
        >
          <option value="A4">A4</option>
          <option value="A5">A5</option>
          <option value="Letter">Letter</option>
          <option value="Custom">自定义</option>
        </select>
      </label>
      <label>
        <span>方向</span>
        <select
          value={documentState.schema.page.orientation}
          onChange={(event) => onCommit(updatePage(documentState, { orientation: event.target.value === "Landscape" ? "Landscape" : "Portrait" }))}
        >
          <option value="Portrait">竖版</option>
          <option value="Landscape">横版</option>
        </select>
      </label>
      <label>
        <span>默认字号</span>
        <input
          type="number"
          min={6}
          max={24}
          value={documentState.schema.page.fontSizePt}
          onChange={(event) => onCommit(updatePage(documentState, { fontSizePt: normalizeNumber(event.target.value, 10) }))}
        />
      </label>
      <label>
        <span>上边距(mm)</span>
        <input
          type="number"
          min={0}
          max={60}
          step={0.5}
          value={documentState.schema.page.marginTopMm}
          onChange={(event) => onCommit(updatePage(documentState, { marginTopMm: normalizeNumber(event.target.value, 0) }))}
        />
      </label>
      <label>
        <span>右边距(mm)</span>
        <input
          type="number"
          min={0}
          max={60}
          step={0.5}
          value={documentState.schema.page.marginRightMm}
          onChange={(event) => onCommit(updatePage(documentState, { marginRightMm: normalizeNumber(event.target.value, 0) }))}
        />
      </label>
      <label>
        <span>下边距(mm)</span>
        <input
          type="number"
          min={0}
          max={60}
          step={0.5}
          value={documentState.schema.page.marginBottomMm}
          onChange={(event) => onCommit(updatePage(documentState, { marginBottomMm: normalizeNumber(event.target.value, 0) }))}
        />
      </label>
      <label>
        <span>左边距(mm)</span>
        <input
          type="number"
          min={0}
          max={60}
          step={0.5}
          value={documentState.schema.page.marginLeftMm}
          onChange={(event) => onCommit(updatePage(documentState, { marginLeftMm: normalizeNumber(event.target.value, 0) }))}
        />
      </label>
      {documentState.schema.page.size === "Custom" ? (
        <>
          <label>
            <span>宽度(mm)</span>
            <input
              type="number"
              min={40}
              max={600}
              step={1}
              value={documentState.schema.page.widthMm ?? 210}
              onChange={(event) => onCommit(updatePage(documentState, { widthMm: normalizeNumber(event.target.value, 210) }))}
            />
          </label>
          <label>
            <span>高度(mm)</span>
            <input
              type="number"
              min={40}
              max={600}
              step={1}
              value={documentState.schema.page.heightMm ?? 297}
              onChange={(event) => onCommit(updatePage(documentState, { heightMm: normalizeNumber(event.target.value, 297) }))}
            />
          </label>
        </>
      ) : null}
    </div>
  );
}

export function SectionPrintProperties({
  documentState,
  onCommit,
}: {
  documentState: ReportDesignerDocumentState;
  onCommit: (nextState: ReportDesignerDocumentState) => void;
}) {
  function updatePrint(sectionId: string, patch: Partial<ReportSection["print"]>) {
    onCommit(updateSectionPrint(documentState, sectionId, patch));
  }

  return (
    <div className="new-report-section-print-list">
      {documentState.schema.sections.map((section) => (
        <div className="new-report-section-print-card" key={section.id}>
          <div className="new-report-section-print-title">
            <strong>{sectionLabel(section)}</strong>
            <small>{section.blocks.length} 个组件</small>
          </div>
          <div className="new-report-property-grid">
            <label>
              <span>最小高度(mm)</span>
              <input
                type="number"
                min={0}
                max={260}
                step={0.5}
                value={section.print.minHeightMm ?? 0}
                onChange={(event) => updatePrint(section.id, { minHeightMm: normalizeNumber(event.target.value, section.print.minHeightMm ?? 0) })}
              />
            </label>
            {section.type !== "Body" ? (
              <label className="new-report-checkbox-label">
                <span>跨页重复</span>
                <input
                  type="checkbox"
                  checked={section.print.repeatOnEveryPage}
                  onChange={(event) => updatePrint(section.id, { repeatOnEveryPage: event.target.checked })}
                />
              </label>
            ) : null}
            <label className="new-report-checkbox-label">
              <span>版区避免拆分</span>
              <input
                type="checkbox"
                checked={section.print.keepTogether}
                onChange={(event) => updatePrint(section.id, { keepTogether: event.target.checked })}
              />
            </label>
            {section.type === "Footer" ? (
              <label className="new-report-checkbox-label">
                <span>短页贴近页底</span>
                <input
                  type="checkbox"
                  checked={Boolean(section.print.pinToPageBottom)}
                  onChange={(event) => updatePrint(section.id, { pinToPageBottom: event.target.checked })}
                />
              </label>
            ) : null}
          </div>
        </div>
      ))}
    </div>
  );
}

export function SelectedSectionProperties({
  section,
  onCommit,
}: {
  section: ReportSection;
  onCommit: (patch: Partial<ReportSection["print"]>) => void;
}) {
  return (
    <div className="new-report-section-selected-properties">
      <div className="new-report-property-readout">
        <span>版区</span>
        <strong>{sectionLabel(section)}</strong>
      </div>
      <div className="new-report-property-readout">
        <span>组件数量</span>
        <strong>{section.blocks.length}</strong>
      </div>
      <div className="new-report-property-grid">
        <label>
          <span>最小高度(mm)</span>
          <input
            type="number"
            min={0}
            max={260}
            step={0.5}
            value={section.print.minHeightMm ?? 0}
            onChange={(event) => onCommit({ minHeightMm: normalizeNumber(event.target.value, section.print.minHeightMm ?? 0) })}
          />
        </label>
        {section.type !== "Body" ? (
          <label className="new-report-checkbox-label">
            <span>跨页重复</span>
            <input
              type="checkbox"
              checked={section.print.repeatOnEveryPage}
              onChange={(event) => onCommit({ repeatOnEveryPage: event.target.checked })}
            />
          </label>
        ) : (
          <div className="new-report-designer-muted">主体版区随内容自然分页，不参与跨页重复。</div>
        )}
        <label className="new-report-checkbox-label">
          <span>版区避免拆分</span>
          <input
            type="checkbox"
            checked={section.print.keepTogether}
            onChange={(event) => onCommit({ keepTogether: event.target.checked })}
          />
        </label>
        {section.type === "Footer" ? (
          <label className="new-report-checkbox-label">
            <span>短页贴近页底</span>
            <input
              type="checkbox"
              checked={Boolean(section.print.pinToPageBottom)}
              onChange={(event) => onCommit({ pinToPageBottom: event.target.checked })}
            />
          </label>
        ) : null}
      </div>
      <div className="new-report-section-block-summary">
        <strong>组件摘要</strong>
        {section.blocks.length === 0 ? (
          <span className="new-report-designer-muted">空版区</span>
        ) : (
          section.blocks.map((block, index) => (
            <span key={block.id}>
              {index + 1}. {blockLabel(block)}
            </span>
          ))
        )}
      </div>
    </div>
  );
}
