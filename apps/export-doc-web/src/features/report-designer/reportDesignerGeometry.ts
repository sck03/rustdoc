import type { ReportDesignerV3Element, ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";

export type ReportDesignerV3ElementBounds = {
  left: number;
  top: number;
  right: number;
  bottom: number;
};

type RotatedBox = {
  xHundredthMm: number;
  yHundredthMm: number;
  widthHundredthMm: number;
  heightHundredthMm: number;
  rotationDeg: number;
};

/** Returns the visual bounds after the element's center-origin rotation. */
export function reportDesignerV3ElementBounds(element: RotatedBox): ReportDesignerV3ElementBounds {
  const width = Math.max(0, Number.isFinite(element.widthHundredthMm) ? element.widthHundredthMm : 0);
  const height = Math.max(0, Number.isFinite(element.heightHundredthMm) ? element.heightHundredthMm : 0);
  const angle = (Number.isFinite(element.rotationDeg) ? element.rotationDeg : 0) * Math.PI / 180;
  const halfWidth = (Math.abs(width * Math.cos(angle)) + Math.abs(height * Math.sin(angle))) / 2;
  const halfHeight = (Math.abs(width * Math.sin(angle)) + Math.abs(height * Math.cos(angle))) / 2;
  const centerX = (Number.isFinite(element.xHundredthMm) ? element.xHundredthMm : 0) + width / 2;
  const centerY = (Number.isFinite(element.yHundredthMm) ? element.yHundredthMm : 0) + height / 2;
  return { left: centerX - halfWidth, top: centerY - halfHeight, right: centerX + halfWidth, bottom: centerY + halfHeight };
}

export function getElementBounds(items: Array<{ element: ReportDesignerV3Element }>) {
  return items.reduce((bounds, item) => {
    const visual = reportDesignerV3ElementBounds(item.element);
    return {
      left: Math.min(bounds.left, visual.left),
      top: Math.min(bounds.top, visual.top),
      right: Math.max(bounds.right, visual.right),
      bottom: Math.max(bounds.bottom, visual.bottom),
    };
  }, {
    left: Number.POSITIVE_INFINITY,
    top: Number.POSITIVE_INFINITY,
    right: Number.NEGATIVE_INFINITY,
    bottom: Number.NEGATIVE_INFINITY,
  });
}

export type ReportDesignerV3MoveConstraint = {
  anchorXHundredthMm: number;
  anchorYHundredthMm: number;
  gridEnabled: boolean;
  gridSizeHundredthMm: number;
  minimumDeltaX: number;
  maximumDeltaX: number;
  minimumDeltaY: number;
  maximumDeltaY: number;
};

/** Pre-compute the invariant geometry once for a pointer-move gesture. */
export function createV3MoveConstraint(
  schema: ReportDesignerV3Schema,
  elements: Iterable<ReportDesignerV3Element>,
): ReportDesignerV3MoveConstraint | null {
  const movable = Array.from(elements);
  if (movable.length === 0) return null;
  const anchor = movable[0];
  const bounds = getElementBounds(movable.map((element) => ({ element })));
  return {
    anchorXHundredthMm: anchor.xHundredthMm,
    anchorYHundredthMm: anchor.yHundredthMm,
    gridEnabled: schema.grid.enabled,
    gridSizeHundredthMm: schema.grid.sizeHundredthMm,
    minimumDeltaX: -bounds.left,
    maximumDeltaX: schema.page.widthHundredthMm - bounds.right,
    minimumDeltaY: -bounds.top,
    maximumDeltaY: schema.page.heightHundredthMm - bounds.bottom,
  };
}

export function resolveV3MoveDeltaFromConstraint(
  constraint: ReportDesignerV3MoveConstraint | null,
  deltaX: number,
  deltaY: number,
  snap = true,
) {
  if (!constraint) return { dx: 0, dy: 0 };
  const grid = constraint.gridEnabled && snap ? constraint.gridSizeHundredthMm : 1;
  const rawDx = grid > 1
    ? snapToGrid(constraint.anchorXHundredthMm + deltaX, grid) - snapToGrid(constraint.anchorXHundredthMm, grid)
    : Math.round(deltaX);
  const rawDy = grid > 1
    ? snapToGrid(constraint.anchorYHundredthMm + deltaY, grid) - snapToGrid(constraint.anchorYHundredthMm, grid)
    : Math.round(deltaY);
  return {
    dx: clampTranslation(Number.isFinite(rawDx) ? rawDx : 0, constraint.minimumDeltaX, constraint.maximumDeltaX),
    dy: clampTranslation(Number.isFinite(rawDy) ? rawDy : 0, constraint.minimumDeltaY, constraint.maximumDeltaY),
  };
}

function clampTranslation(value: number, minimum: number, maximum: number) {
  if (minimum > maximum) return 0;
  return Math.min(maximum, Math.max(minimum, value));
}

function snapToGrid(value: number, grid: number) {
  return grid <= 1 ? Math.round(value) : Math.round(value / grid) * grid;
}
