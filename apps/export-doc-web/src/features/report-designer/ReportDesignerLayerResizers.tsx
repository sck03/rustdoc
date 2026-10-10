import { useLayoutEffect, useRef, type KeyboardEvent, type PointerEvent } from "react";
import { clampReportDesignerLayerHeight, isReportDesignerLayerRoleLocked, resolveReportDesignerLayerBands } from "./reportDesignerLayerBands.ts";
import type { ReportDesignerV3LayerRole, ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";
import { useReportDesignerPointerGesture } from "./useReportDesignerPointerGesture.ts";

type BandRole = Extract<ReportDesignerV3LayerRole, "Header" | "Footer">;

export function ReportDesignerLayerResizers({ schema, zoom, disabled, onCommit }: {
  schema: ReportDesignerV3Schema;
  zoom: number;
  disabled: boolean;
  onCommit: (role: BandRole, heightHundredthMm: number) => void;
}) {
  const pointer = useReportDesignerPointerGesture();
  const schemaRef = useRef(schema);
  schemaRef.current = schema;
  const bands = resolveReportDesignerLayerBands(schema);
  const roles: Array<[BandRole, number]> = [["Header", bands.headerHeight], ["Footer", bands.footerHeight]];
  useLayoutEffect(() => { pointer.cancel(); }, [schema, disabled, zoom, pointer]);

  function begin(event: PointerEvent<HTMLDivElement>, role: BandRole, height: number) {
    if (disabled || event.button !== 0 || pointer.isActive() || isReportDesignerLayerRoleLocked(schema, role)) return;
    const page = event.currentTarget.parentElement;
    if (!page) return;
    const rect = page.getBoundingClientRect();
    if (rect.height <= 0) return;
    event.preventDefault();
    event.stopPropagation();
    const startY = event.clientY;
    const scale = schema.page.heightHundredthMm / rect.height * (role === "Header" ? 1 : -1);
    const nextHeight = (y: number) => clampReportDesignerLayerHeight(schema, role, height + (y - startY) * scale);
    const variable = role === "Header" ? "--v3-header-band-height" : "--v3-footer-band-height";
    pointer.start(event, {
      surface: page,
      preview: ({ y }) => page.style.setProperty(variable, `${nextHeight(y) / 100}mm`),
      commit: ({ y }) => onCommit(role, nextHeight(y)),
      restore: () => {
        const current = resolveReportDesignerLayerBands(schemaRef.current);
        page.style.setProperty(variable, `${(role === "Header" ? current.headerHeight : current.footerHeight) / 100}mm`);
      },
    });
  }

  function resizeWithKeyboard(event: KeyboardEvent<HTMLDivElement>, role: BandRole, height: number) {
    if (disabled || isReportDesignerLayerRoleLocked(schema, role)) return;
    const direction = event.key === "ArrowDown" ? 1 : event.key === "ArrowUp" ? -1 : 0;
    if (!direction) return;
    event.preventDefault();
    event.stopPropagation();
    const delta = role === "Header" ? direction * 100 : -direction * 100;
    onCommit(role, clampReportDesignerLayerHeight(schema, role, height + delta));
  }

  return <>{roles.map(([role, height]) => schema.layers.some((layer) => layer.role === role && layer.visible) ? (
    <div
      key={role}
      className={`report-designer-v3-band-resizer report-designer-v3-band-resizer-${role.toLowerCase()}`}
      role="separator"
      tabIndex={disabled || isReportDesignerLayerRoleLocked(schema, role) ? -1 : 0}
      aria-disabled={disabled || isReportDesignerLayerRoleLocked(schema, role) || undefined}
      aria-label={`调整${role === "Header" ? "页眉" : "页脚"}设计区高度`}
      aria-orientation="horizontal"
      aria-valuemin={0}
      aria-valuemax={clampReportDesignerLayerHeight(schema, role, schema.page.heightHundredthMm) / 100}
      aria-valuenow={height / 100}
      title={`拖动调整${role === "Header" ? "页眉" : "页脚"}高度`}
      onPointerDown={(event) => begin(event, role, height)}
      onKeyDown={(event) => resizeWithKeyboard(event, role, height)}
    ><span>{(height / 100).toFixed(1)} mm</span></div>
  ) : null)}</>;
}
