import type { CSSProperties } from "react";
import { hundredthMmToMm, type ReportDesignerV3Layer, type ReportDesignerV3LayerRole, type ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";
import type { ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";

export const REPORT_DESIGNER_MIN_BODY_BAND_HUNDREDTH_MM = 1000;

export function defaultReportDesignerLayerHeight(layer: Pick<ReportDesignerV3Layer, "role" | "print">) {
  if (layer.role === "Header") return Math.max(1800, layer.print.minHeightHundredthMm);
  if (layer.role === "Footer") return Math.max(1400, layer.print.minHeightHundredthMm);
  return 0;
}

export function reportDesignerLayerHeight(layer: ReportDesignerV3Layer) {
  const value = layer.designHeightHundredthMm;
  return Number.isFinite(value) ? Math.max(0, Math.round(value as number)) : defaultReportDesignerLayerHeight(layer);
}

export function resolveReportDesignerLayerBands(schema: ReportDesignerV3Schema) {
  const pageHeight = schema.page.heightHundredthMm;
  const bodyVisible = schema.layers.some((layer) => layer.role === "Body" && layer.visible);
  const available = Math.max(0, pageHeight - (bodyVisible ? REPORT_DESIGNER_MIN_BODY_BAND_HUNDREDTH_MM : 0));
  const rawHeader = largestVisibleBand(schema.layers, "Header");
  const rawFooter = largestVisibleBand(schema.layers, "Footer");
  const scale = rawHeader + rawFooter > available && rawHeader + rawFooter > 0 ? available / (rawHeader + rawFooter) : 1;
  const headerHeight = Math.round(rawHeader * scale);
  const footerHeight = Math.round(rawFooter * scale);
  return {
    headerHeight,
    bodyHeight: bodyVisible ? Math.max(0, pageHeight - headerHeight - footerHeight) : 0,
    footerHeight,
    pageHeight,
  };
}

export function reportDesignerLayerBandStyle(schema: ReportDesignerV3Schema): CSSProperties {
  const bands = resolveReportDesignerLayerBands(schema);
  return {
    "--v3-header-band-height": `${hundredthMmToMm(bands.headerHeight)}mm`,
    "--v3-footer-band-height": `${hundredthMmToMm(bands.footerHeight)}mm`,
  } as CSSProperties;
}

export function clampReportDesignerLayerHeight(
  schema: ReportDesignerV3Schema,
  role: Extract<ReportDesignerV3LayerRole, "Header" | "Footer">,
  value: number,
) {
  const bands = resolveReportDesignerLayerBands(schema);
  const other = role === "Header" ? bands.footerHeight : bands.headerHeight;
  const bodyVisible = schema.layers.some((layer) => layer.role === "Body" && layer.visible);
  const maximum = Math.max(0, schema.page.heightHundredthMm - other - (bodyVisible ? REPORT_DESIGNER_MIN_BODY_BAND_HUNDREDTH_MM : 0));
  return Math.min(maximum, Math.max(0, Math.round(value)));
}

export function setReportDesignerLayerHeight(state: ReportDesignerV3DocumentState, layerId: string, value: number) {
  const layer = state.schema.layers.find((candidate) => candidate.id === layerId);
  if (!layer || (layer.role !== "Header" && layer.role !== "Footer")) return state;
  const nextHeight = clampReportDesignerLayerHeight(state.schema, layer.role, value);
  if (reportDesignerLayerHeight(layer) === nextHeight && layer.designHeightHundredthMm !== undefined) return state;
  return {
    ...state,
    schema: {
      ...state.schema,
      layers: state.schema.layers.map((candidate) => candidate.id === layerId ? { ...candidate, designHeightHundredthMm: nextHeight } : candidate),
    },
  };
}

export function setReportDesignerLayerRoleHeight(
  state: ReportDesignerV3DocumentState,
  role: Extract<ReportDesignerV3LayerRole, "Header" | "Footer">,
  value: number,
) {
  const nextHeight = clampReportDesignerLayerHeight(state.schema, role, value);
  const layers = state.schema.layers.map((layer) => layer.role === role && layer.visible
    ? { ...layer, designHeightHundredthMm: nextHeight }
    : layer);
  return layers.every((layer, index) => layer === state.schema.layers[index]) ? state : { ...state, schema: { ...state.schema, layers } };
}

function largestVisibleBand(layers: ReportDesignerV3Layer[], role: ReportDesignerV3LayerRole) {
  return Math.max(0, ...layers.filter((layer) => layer.role === role && layer.visible).map(reportDesignerLayerHeight));
}
