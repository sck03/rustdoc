import { useRef, type KeyboardEvent, type PointerEvent } from "react";
import { clampReportDesignerLayerHeight, resolveReportDesignerLayerBands } from "./reportDesignerLayerBands.ts";
import type { ReportDesignerV3LayerRole, ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";

type BandRole = Extract<ReportDesignerV3LayerRole, "Header" | "Footer">;
type DragState = { pointerId: number; role: BandRole; startY: number; startHeight: number; nextHeight: number; page: HTMLElement };

export function ReportDesignerLayerResizers({ schema, disabled, onCommit }: {
  schema: ReportDesignerV3Schema;
  disabled: boolean;
  onCommit: (role: BandRole, heightHundredthMm: number) => void;
}) {
  const drag = useRef<DragState | null>(null);
  const bands = resolveReportDesignerLayerBands(schema);
  const roles: Array<[BandRole, number]> = [["Header", bands.headerHeight], ["Footer", bands.footerHeight]];

  function begin(event: PointerEvent<HTMLDivElement>, role: BandRole, height: number) {
    if (disabled || event.button !== 0) return;
    const page = event.currentTarget.parentElement;
    if (!page) return;
    event.preventDefault();
    event.stopPropagation();
    drag.current = { pointerId: event.pointerId, role, startY: event.clientY, startHeight: height, nextHeight: height, page };
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function move(event: PointerEvent<HTMLDivElement>) {
    const current = drag.current;
    if (!current || current.pointerId !== event.pointerId) return;
    const rect = current.page.getBoundingClientRect();
    if (rect.height <= 0) return;
    const delta = Math.round(((event.clientY - current.startY) / rect.height) * schema.page.heightHundredthMm);
    current.nextHeight = clampReportDesignerLayerHeight(schema, current.role, current.startHeight + (current.role === "Header" ? delta : -delta));
    current.page.style.setProperty(current.role === "Header" ? "--v3-header-band-height" : "--v3-footer-band-height", `${current.nextHeight / 100}mm`);
  }

  function finish(event: PointerEvent<HTMLDivElement>, cancelled = false) {
    const current = drag.current;
    if (!current || current.pointerId !== event.pointerId) return;
    drag.current = null;
    if (cancelled) {
      current.page.style.setProperty(current.role === "Header" ? "--v3-header-band-height" : "--v3-footer-band-height", `${current.startHeight / 100}mm`);
      return;
    }
    onCommit(current.role, current.nextHeight);
  }

  function resizeWithKeyboard(event: KeyboardEvent<HTMLDivElement>, role: BandRole, height: number) {
    const direction = event.key === "ArrowDown" ? 1 : event.key === "ArrowUp" ? -1 : 0;
    if (!direction) return;
    event.preventDefault();
    const delta = role === "Header" ? direction * 100 : -direction * 100;
    onCommit(role, clampReportDesignerLayerHeight(schema, role, height + delta));
  }

  return <>{roles.map(([role, height]) => schema.layers.some((layer) => layer.role === role && layer.visible) ? (
    <div
      key={role}
      className={`report-designer-v3-band-resizer report-designer-v3-band-resizer-${role.toLowerCase()}`}
      role="separator"
      tabIndex={disabled ? -1 : 0}
      aria-label={`调整${role === "Header" ? "页眉" : "页脚"}设计区高度`}
      aria-orientation="horizontal"
      aria-valuemin={0}
      aria-valuemax={schema.page.heightHundredthMm / 100}
      aria-valuenow={height / 100}
      title={`拖动调整${role === "Header" ? "页眉" : "页脚"}高度`}
      onPointerDown={(event) => begin(event, role, height)}
      onPointerMove={move}
      onPointerUp={finish}
      onPointerCancel={(event) => finish(event, true)}
      onKeyDown={(event) => resizeWithKeyboard(event, role, height)}
    ><span>{(height / 100).toFixed(1)} mm</span></div>
  ) : null)}</>;
}
