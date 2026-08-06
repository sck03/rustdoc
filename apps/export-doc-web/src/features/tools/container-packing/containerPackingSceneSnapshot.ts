export const containerPackingSceneSnapshotEvent = "exportdoc:capture-container-packing-scene";

export type ContainerPackingSceneSnapshotDetail = {
  dataUrl: string | null;
};

export function captureContainerPackingSceneSnapshot(root: HTMLElement) {
  const canvas = root.querySelector<HTMLCanvasElement>(".container-packing-3d-canvas");
  if (!canvas) {
    return null;
  }

  const event = new CustomEvent<ContainerPackingSceneSnapshotDetail>(containerPackingSceneSnapshotEvent, {
    detail: { dataUrl: null },
  });
  canvas.dispatchEvent(event);
  return event.detail.dataUrl;
}
