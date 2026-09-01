import {
  clampReportDesignerV3ElementToPage,
  REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM,
  type ReportDesignerV3Element,
  type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";
import type {
  ReportDesignerV3DocumentState,
  ReportDesignerV3ResizeDirection,
} from "./reportDesignerV3Mutations.ts";

/**
 * Resize one free-canvas element in its local (rotated) coordinate system.
 * Pointer deltas arrive in page coordinates, so resolving them through the
 * inverse rotation keeps east/west/north/south handles intuitive at any angle.
 */
export function resizeV3Element(
  state: ReportDesignerV3DocumentState,
  elementId: string,
  direction: ReportDesignerV3ResizeDirection,
  deltaX: number,
  deltaY: number,
): ReportDesignerV3DocumentState {
  const located = findElement(state.schema, elementId);
  if (!located || located.element.locked || located.layer.locked) return state;

  const minimum = REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM;
  const width = Math.max(minimum, finiteOr(located.element.widthHundredthMm, minimum));
  const height = Math.max(minimum, finiteOr(located.element.heightHundredthMm, minimum));
  const angle = finiteOr(located.element.rotationDeg, 0) * Math.PI / 180;
  const cos = Math.cos(angle);
  const sin = Math.sin(angle);
  const pageDeltaX = finiteOr(deltaX, 0);
  const pageDeltaY = finiteOr(deltaY, 0);
  const localDeltaX = cos * pageDeltaX + sin * pageDeltaY;
  const localDeltaY = -sin * pageDeltaX + cos * pageDeltaY;

  let left = 0;
  let top = 0;
  let right = width;
  let bottom = height;
  if (direction.includes("w")) left += localDeltaX;
  if (direction.includes("e")) right += localDeltaX;
  if (direction.includes("n")) top += localDeltaY;
  if (direction.includes("s")) bottom += localDeltaY;
  if (right - left < minimum) {
    if (direction.includes("w")) left = right - minimum;
    else right = left + minimum;
  }
  if (bottom - top < minimum) {
    if (direction.includes("n")) top = bottom - minimum;
    else bottom = top + minimum;
  }

  const nextWidth = Math.max(minimum, right - left);
  const nextHeight = Math.max(minimum, bottom - top);
  const localCenterShiftX = (left + right - width) / 2;
  const localCenterShiftY = (top + bottom - height) / 2;
  const oldCenterX = finiteOr(located.element.xHundredthMm, 0) + width / 2;
  const oldCenterY = finiteOr(located.element.yHundredthMm, 0) + height / 2;
  const nextCenterX = oldCenterX + cos * localCenterShiftX - sin * localCenterShiftY;
  const nextCenterY = oldCenterY + sin * localCenterShiftX + cos * localCenterShiftY;
  const resized = clampReportDesignerV3ElementToPage({
    ...located.element,
    xHundredthMm: Math.round(nextCenterX - nextWidth / 2),
    yHundredthMm: Math.round(nextCenterY - nextHeight / 2),
    widthHundredthMm: Math.round(nextWidth),
    heightHundredthMm: Math.round(nextHeight),
  }, state.schema.page);

  if (sameGeometry(located.element, resized)) return state;
  return {
    ...state,
    schema: {
      ...state.schema,
      layers: state.schema.layers.map((layer) => layer.id === located.layer.id
        ? { ...layer, elements: layer.elements.map((element) => element.id === elementId ? resized : element) }
        : layer),
    },
  };
}

function findElement(schema: ReportDesignerV3Schema, elementId: string) {
  for (const layer of schema.layers) {
    const element = layer.elements.find((candidate) => candidate.id === elementId);
    if (element) return { element, layer };
  }
  return null;
}

function finiteOr(value: number, fallback: number) {
  return Number.isFinite(value) ? value : fallback;
}

function sameGeometry(left: ReportDesignerV3Element, right: ReportDesignerV3Element) {
  return left.xHundredthMm === right.xHundredthMm &&
    left.yHundredthMm === right.yHundredthMm &&
    left.widthHundredthMm === right.widthHundredthMm &&
    left.heightHundredthMm === right.heightHundredthMm;
}
