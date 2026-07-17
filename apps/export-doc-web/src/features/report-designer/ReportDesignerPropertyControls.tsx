import { type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent, useId } from "react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBorderStyle, ReportTextStyle } from "./reportDesignerSchema.ts";
import { normalizeDesignerFieldPath } from "./reportDesignerMutations.ts";
import {
  formatDesignerWidth,
  normalizeAlign,
  normalizeBorderForEditor,
  normalizeBorderLineStyle,
  normalizeNumber,
  roundDesignerWidth,
} from "./reportDesignerPropertiesModel.ts";

export function FieldPathInput({
  label,
  value,
  fieldGroups,
  className,
  onChange,
}: {
  label: string;
  value: string;
  fieldGroups: ReportDesignerFieldGroup[];
  className?: string;
  onChange: (fieldPath: string) => void;
}) {
  const listId = useId();
  const fields = fieldGroups.flatMap((group) => group.fields.map((field) => ({ ...field, category: group.category })));

  return (
    <label className={className}>
      <span>{label}</span>
      <input
        value={value}
        list={listId}
        onChange={(event) => onChange(normalizeDesignerFieldPath(event.target.value))}
      />
      <datalist id={listId}>
        {fields.map((field) => (
          <option key={`${field.category}-${field.value}`} value={field.value}>
            {field.category} / {field.label}
          </option>
        ))}
      </datalist>
    </label>
  );
}

export function ColumnWidthStrip({
  columns,
  unit,
  minWidth,
  onResizeBoundary,
}: {
  columns: Array<{ id: string; title: string; width: number }>;
  unit: "%" | "mm";
  minWidth: number;
  onResizeBoundary: (leftColumnId: string, delta: number) => void;
}) {
  const safeColumns = columns.filter((column) => Number.isFinite(column.width) && column.width > 0);
  if (safeColumns.length === 0) {
    return null;
  }

  const totalWidth = safeColumns.reduce((sum, column) => sum + Math.max(minWidth, column.width), 0);
  let consumedWidth = 0;
  const boundaries = safeColumns.slice(0, -1).map((column) => {
    consumedWidth += Math.max(minWidth, column.width);
    return {
      column,
      leftPercent: totalWidth > 0 ? (consumedWidth / totalWidth) * 100 : 0,
    };
  });

  function startResize(event: ReactPointerEvent<HTMLButtonElement>, leftColumnId: string) {
    const stripElement = event.currentTarget.closest(".new-report-column-width-strip");
    if (!(stripElement instanceof HTMLElement)) {
      return;
    }

    const rect = stripElement.getBoundingClientRect();
    if (rect.width <= 0 || totalWidth <= 0) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    const startClientX = event.clientX;
    const pixelsPerUnit = rect.width / totalWidth;

    function handlePointerMove(nativeEvent: globalThis.PointerEvent) {
      nativeEvent.preventDefault();
      const delta = (nativeEvent.clientX - startClientX) / pixelsPerUnit;
      onResizeBoundary(leftColumnId, roundDesignerWidth(delta));
    }

    function stopResize() {
      document.removeEventListener("pointermove", handlePointerMove);
      document.removeEventListener("pointerup", stopResize);
      document.removeEventListener("pointercancel", stopResize);
    }

    document.addEventListener("pointermove", handlePointerMove);
    document.addEventListener("pointerup", stopResize);
    document.addEventListener("pointercancel", stopResize);
  }

  function handleBoundaryKeyDown(
    event: ReactKeyboardEvent<HTMLButtonElement>,
    leftColumnId: string,
  ) {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") {
      return;
    }

    event.preventDefault();
    const direction = event.key === "ArrowLeft" ? -1 : 1;
    const step = event.shiftKey ? 5 : unit === "mm" ? 1 : 0.5;
    onResizeBoundary(leftColumnId, direction * step);
  }

  return (
    <div className="new-report-column-width-editor">
      <div className="new-report-column-width-strip" role="group" aria-label="列宽可视化调整">
        {safeColumns.map((column, index) => (
          <div
            className="new-report-column-width-segment"
            key={column.id}
            style={{ flex: `${Math.max(minWidth, column.width)} 1 0` }}
            title={`${column.title}: ${formatDesignerWidth(column.width)}${unit}`}
          >
            <span>{index + 1}</span>
            <strong>{formatDesignerWidth(column.width)}{unit}</strong>
          </div>
        ))}
        {boundaries.map((boundary, index) => (
          <button
            aria-label={`调整第 ${index + 1} 列和第 ${index + 2} 列宽度`}
            className="new-report-column-width-handle"
            key={`${boundary.column.id}-handle`}
            onKeyDown={(event) => handleBoundaryKeyDown(event, boundary.column.id)}
            onPointerDown={(event) => startResize(event, boundary.column.id)}
            style={{ left: `${boundary.leftPercent}%` }}
            title="拖动调整相邻两列宽度，方向键微调"
            type="button"
          />
        ))}
      </div>
    </div>
  );
}

export function BorderEditor({
  border,
  onChange,
}: {
  border?: ReportBorderStyle;
  onChange: (border: ReportBorderStyle) => void;
}) {
  const current = normalizeBorderForEditor(border);

  function update(patch: Partial<ReportBorderStyle>) {
    onChange({
      ...current,
      ...patch,
    });
  }

  return (
    <div className="new-report-detail-style-group">
      <div className="new-report-detail-column-title">
        <strong>边框</strong>
      </div>
      <div className="new-report-property-grid">
        <label>
          <span>颜色</span>
          <input type="color" value={current.color} onChange={(event) => update({ color: event.target.value })} />
        </label>
        <label>
          <span>粗细(px)</span>
          <input
            type="number"
            min={0}
            max={8}
            step={1}
            value={current.widthPx}
            onChange={(event) => update({ widthPx: normalizeNumber(event.target.value, current.widthPx) })}
          />
        </label>
        <label>
          <span>线型</span>
          <select
            value={current.style ?? "Solid"}
            onChange={(event) => update({ style: normalizeBorderLineStyle(event.target.value) })}
          >
            <option value="Solid">实线</option>
            <option value="Dashed">虚线</option>
            <option value="None">无边框</option>
          </select>
        </label>
        <label className="new-report-checkbox-label">
          <span>上边</span>
          <input type="checkbox" checked={Boolean(current.top)} onChange={(event) => update({ top: event.target.checked })} />
        </label>
        <label className="new-report-checkbox-label">
          <span>右边</span>
          <input type="checkbox" checked={Boolean(current.right)} onChange={(event) => update({ right: event.target.checked })} />
        </label>
        <label className="new-report-checkbox-label">
          <span>下边</span>
          <input type="checkbox" checked={Boolean(current.bottom)} onChange={(event) => update({ bottom: event.target.checked })} />
        </label>
        <label className="new-report-checkbox-label">
          <span>左边</span>
          <input type="checkbox" checked={Boolean(current.left)} onChange={(event) => update({ left: event.target.checked })} />
        </label>
      </div>
    </div>
  );
}

export function TextStyleEditor({
  style,
  onChange,
}: {
  style: ReportTextStyle;
  onChange: (style: ReportTextStyle) => void;
}) {
  return (
    <div className="new-report-property-grid new-report-style-grid">
      <label>
        <span>字号</span>
        <input
          type="number"
          min={6}
          max={48}
          step={0.5}
          value={style.fontSizePt ?? 10}
          onChange={(event) => onChange({ ...style, fontSizePt: normalizeNumber(event.target.value, 10) })}
        />
      </label>
      <label>
        <span>对齐</span>
        <select
          value={style.align ?? "Left"}
          onChange={(event) => onChange({ ...style, align: normalizeAlign(event.target.value) })}
        >
          <option value="Left">左</option>
          <option value="Center">中</option>
          <option value="Right">右</option>
        </select>
      </label>
      <label className="new-report-checkbox-label">
        <span>加粗</span>
        <input
          type="checkbox"
          checked={Boolean(style.bold)}
          onChange={(event) => onChange({ ...style, bold: event.target.checked })}
        />
      </label>
      <label>
        <span>上距(mm)</span>
        <input
          type="number"
          min={0}
          max={30}
          step={0.5}
          value={style.marginTopMm ?? 0}
          onChange={(event) => onChange({ ...style, marginTopMm: normalizeNumber(event.target.value, 0) })}
        />
      </label>
      <label>
        <span>下距(mm)</span>
        <input
          type="number"
          min={0}
          max={30}
          step={0.5}
          value={style.marginBottomMm ?? 0}
          onChange={(event) => onChange({ ...style, marginBottomMm: normalizeNumber(event.target.value, 0) })}
        />
      </label>
    </div>
  );
}
