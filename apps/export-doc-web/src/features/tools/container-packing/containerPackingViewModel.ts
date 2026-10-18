// Keep the on-screen overview aligned with the Rust job-sheet view: 30° yaw, 28° elevation.
export const containerPackingViewAngles = {
  isometric: { x: 28 * Math.PI / 180, y: -Math.PI / 6 },
  top: { x: Math.PI / 2, y: 0 },
  side: { x: 0, y: 0 },
  door: { x: 0, y: -Math.PI / 2 },
};
export type ContainerPackingViewPreset = keyof typeof containerPackingViewAngles;
