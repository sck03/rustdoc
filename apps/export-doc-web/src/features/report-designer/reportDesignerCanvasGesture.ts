export function findReportDesignerElementNodes(canvas: HTMLDivElement | null, ids: Iterable<string>) {
  const result = new Map<string, HTMLElement>();
  if (!canvas) return result;
  for (const id of ids) {
    const node = canvas.querySelector<HTMLElement>(`[data-v3-element-id="${CSS.escape(id)}"]`);
    if (node) result.set(id, node);
  }
  return result;
}

export function prepareReportDesignerGestureNodes(
  nodes: Map<string, HTMLElement>,
  transform: "move" | "resize",
) {
  const willChange = transform === "move" ? "transform" : "left, top, width, height, transform";
  for (const node of nodes.values()) node.style.willChange = willChange;
}

export function releaseReportDesignerGestureNodes(nodes: Map<string, HTMLElement>) {
  for (const node of nodes.values()) node.style.willChange = "";
}

export function readReportDesignerGridCellId(target: EventTarget | null) {
  return target instanceof Element
    ? target.closest<HTMLElement>("[data-report-grid-cell-id]")?.dataset.reportGridCellId
    : undefined;
}
