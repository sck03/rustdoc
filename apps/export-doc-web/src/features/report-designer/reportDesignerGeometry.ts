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
