import { type ChangeEvent as ReactChangeEvent, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent, type ReactNode, useEffect, useId, useRef, useState } from "react";
import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportBorderStyle, ReportTextStyle } from "./reportDesignerSchema.ts";
import { normalizeDesignerFieldPath } from "./reportDesignerMutations.ts";
import { resizeAdjacentWidths } from "./reportDesignerTableMutations.ts";
import {
  formatDesignerWidth,
  normalizeAlign,
  normalizeBorderForEditor,
  normalizeBorderLineStyle,
  normalizeNumber,
  roundDesignerWidth,
} from "./reportDesignerPropertiesModel.ts";

export type SelectOption = { value: string; label: string };

export function DesignerCheckbox({ checked, mixed = false, disabled = false, onChange, children }: { checked: boolean; mixed?: boolean; disabled?: boolean; onChange: (checked: boolean) => void; children: ReactNode }) {
  const inputRef = useRef<HTMLInputElement>(null);
  useEffect(() => { if (inputRef.current) inputRef.current.indeterminate = mixed; }, [mixed]);
  return <label className="checkbox-field report-designer-checkbox"><input ref={inputRef} type="checkbox" checked={checked} aria-checked={mixed ? "mixed" : checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} /><span>{children}</span></label>;
}

export function DesignerPropertyTabs<T extends string>({ value, options, onChange, children }: { value: T; options: readonly { value: T; label: string }[]; onChange: (value: T) => void; children: ReactNode }) {
  const id = useId();
  return <div className="report-designer-property-tabs"><div role="tablist" aria-label="属性分类">
    {options.map((option, index) => <button key={option.value} id={`${id}-${option.value}`} type="button" role="tab" tabIndex={option.value === value ? 0 : -1} aria-selected={option.value === value} aria-controls={`${id}-panel`} onClick={() => onChange(option.value)} onKeyDown={(event) => {
      const next = event.key === "ArrowRight" ? (index + 1) % options.length : event.key === "ArrowLeft" ? (index + options.length - 1) % options.length : event.key === "Home" ? 0 : event.key === "End" ? options.length - 1 : -1;
      if (next < 0) return;
      event.preventDefault();
      onChange(options[next].value);
      (event.currentTarget.parentElement?.children[next] as HTMLElement | undefined)?.focus();
    }}>{option.label}</button>)}
  </div><div role="tabpanel" id={`${id}-panel`} aria-labelledby={`${id}-${value}`}>{children}</div></div>;
}

/** Keep a damaged/removed persisted value visible until the user repairs it. */
export function ensureCurrentSelectOption(options: SelectOption[], value: string): SelectOption[] {
  if (!value || options.some((option) => option.value === value)) {
    return options;
  }

  return [{ value, label: `当前值：${value}（需修正）` }, ...options];
}

export function FieldPathInput({
  label,
  value,
  fieldGroups,
  className,
  selectOnly = false,
  onChange,
}: {
  label: string;
  value: string;
  fieldGroups: ReportDesignerFieldGroup[];
  className?: string;
  selectOnly?: boolean;
  onChange: (fieldPath: string) => void;
}) {
  const listId = useId();
  const fields = fieldGroups.flatMap((group) => group.fields.map((field) => ({ ...field, category: group.category })));
  const hasKnownValue = fields.some((field) => field.value === value);

  return (
    <label className={className}>
      <span>{label}</span>
      {selectOnly ? (
        <select
          aria-invalid={value !== "" && !hasKnownValue ? true : undefined}
          value={value}
          onChange={(event) => onChange(normalizeDesignerFieldPath(event.target.value))}
        >
          <option value="">请选择字段</option>
          {value && !hasKnownValue ? <option value={value}>当前值：{value}（需修正）</option> : null}
          {fieldGroups.map((group) => (
            <optgroup key={group.category} label={group.category}>
              {group.fields.map((field) => (
                <option key={`${group.category}-${field.value}`} value={field.value}>{field.label}</option>
              ))}
            </optgroup>
          ))}
        </select>
      ) : (
        <>
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
        </>
      )}
    </label>
  );
}

/** Commit text after the user finishes editing so one field is one undo step. */
export function CommitTextField({
  value,
  onCommit,
  disabled = false,
  multiline = false,
  placeholder,
  rows = 3,
}: {
  value: string;
  onCommit: (value: string) => void;
  disabled?: boolean;
  multiline?: boolean;
  placeholder?: string;
  rows?: number;
}) {
  const [draft, setDraft] = useState(value);
  const cancelOnBlur = useRef(false);
  useEffect(() => setDraft(value), [value]);
  function commit() {
    if (cancelOnBlur.current) { cancelOnBlur.current = false; return; }
    if (draft !== value) onCommit(draft);
  }
  const onChange = (event: ReactChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setDraft(event.target.value);
  const onKeyDown = (event: ReactKeyboardEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    if (event.nativeEvent.isComposing) return;
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      cancelOnBlur.current = true;
      setDraft(value);
      event.currentTarget.blur();
    } else if (event.key === "Enter" && (!multiline || event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      event.currentTarget.blur();
    }
  };
  return multiline
    ? <textarea disabled={disabled} placeholder={placeholder} rows={rows} value={draft} onChange={onChange} onBlur={commit} onKeyDown={onKeyDown} />
    : <input type="text" disabled={disabled} placeholder={placeholder} value={draft} onChange={onChange} onBlur={commit} onKeyDown={onKeyDown} />;
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
  const cancelDrag = useRef<() => void>(() => undefined);
  const latest = useRef({ columns, onResizeBoundary });
  latest.current = { columns, onResizeBoundary };
  useEffect(() => () => cancelDrag.current(), []);
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
    if (event.button !== 0) return;
    const strip = event.currentTarget.closest<HTMLElement>(".new-report-column-width-strip");
    const rect = strip?.getBoundingClientRect();
    if (!strip || !rect || rect.width <= 0 || totalWidth <= 0) return;
    event.preventDefault();
    event.stopPropagation();
    cancelDrag.current();
    const startX = event.clientX;
    const stripWidth = rect.width;
    const pointerId = event.pointerId;
    const handle = event.currentTarget;
    const segments = new Map(Array.from(strip.querySelectorAll<HTMLElement>("[data-column-id]")).map((node) => [node.dataset.columnId ?? "", { node, label: node.querySelector("strong") }]));
    const baseWidths = safeColumns.map((column) => column.width).join(",");
    let delta = 0;
    let frame: number | null = null;
    let settled = false;

    function paint(values: typeof safeColumns) {
      let offset = 0;
      const total = values.reduce((sum, column) => sum + column.width, 0);
      for (const column of values) {
        const segment = segments.get(column.id);
        if (segment) {
          const { node, label } = segment;
          node.style.flex = `${column.width} 1 0`;
          if (label) label.textContent = `${formatDesignerWidth(column.width)}${unit}`;
        }
        offset += column.width;
        if (column.id === leftColumnId && total > 0) handle.style.left = `${offset / total * 100}%`;
      }
    }
    function preview() {
      frame = null;
      paint(resizeAdjacentWidths(safeColumns, leftColumnId, delta, minWidth, "width"));
    }
    function move(native: PointerEvent) {
      if (native.pointerId !== pointerId) return;
      delta = roundDesignerWidth((native.clientX - startX) * totalWidth / stripWidth);
      if (frame === null) frame = requestAnimationFrame(preview);
    }
    function finish(commit: boolean, native?: PointerEvent) {
      if (settled || (native && native.pointerId !== pointerId)) return;
      settled = true;
      if (native && commit) delta = roundDesignerWidth((native.clientX - startX) * totalWidth / stripWidth);
      if (frame !== null) cancelAnimationFrame(frame);
      document.removeEventListener("pointermove", move);
      document.removeEventListener("pointerup", up);
      document.removeEventListener("pointercancel", cancel);
      document.removeEventListener("keydown", keyDown, true);
      window.removeEventListener("blur", cancel);
      cancelDrag.current = () => undefined;
      paint(latest.current.columns);
      if (commit && delta !== 0 && latest.current.columns.map((column) => column.width).join(",") === baseWidths) {
        latest.current.onResizeBoundary(leftColumnId, delta);
      }
    }
    const up = (native: PointerEvent) => finish(true, native);
    const cancel = (native?: Event) => { if (!(native instanceof PointerEvent) || native.pointerId === pointerId) finish(false); };
    const keyDown = (native: KeyboardEvent) => {
      if (native.key === "Escape") { native.preventDefault(); native.stopPropagation(); cancel(); }
    };
    cancelDrag.current = cancel;
    document.addEventListener("pointermove", move);
    document.addEventListener("pointerup", up);
    document.addEventListener("pointercancel", cancel);
    document.addEventListener("keydown", keyDown, true);
    window.addEventListener("blur", cancel);
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
            data-column-id={column.id}
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
        <DesignerCheckbox checked={Boolean(current.top)} onChange={(checked) => update({ top: checked })}>上边</DesignerCheckbox>
        <DesignerCheckbox checked={Boolean(current.right)} onChange={(checked) => update({ right: checked })}>右边</DesignerCheckbox>
        <DesignerCheckbox checked={Boolean(current.bottom)} onChange={(checked) => update({ bottom: checked })}>下边</DesignerCheckbox>
        <DesignerCheckbox checked={Boolean(current.left)} onChange={(checked) => update({ left: checked })}>左边</DesignerCheckbox>
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
      <DesignerCheckbox checked={Boolean(style.bold)}
          onChange={(checked) => onChange({ ...style, bold: checked })}>加粗</DesignerCheckbox>
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
