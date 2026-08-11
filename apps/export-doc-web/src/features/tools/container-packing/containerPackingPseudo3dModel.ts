import type { ApiPackedCargoItemDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";
import type { ContainerPackingFormState } from "./containerPackingModel.ts";
import {
  readPositiveNumberInput,
  shadeHexColor,
  signedArgbToColorHex,
} from "./containerPackingModel.ts";
import { mergePackedItemsForContainerRender } from "./containerPackingRenderBlocks.ts";
import {
  clampContainerPackingGridSegments,
  clampNumber,
} from "./containerPackingRenderMath.ts";
import type {
  ContainerPackingRenderBlockSource,
  ContainerPackingVisualizationDimensions,
} from "./containerPackingVisualizationModel.ts";

type ContainerPackingPseudo3dPoint = { x: number; y: number };
export type ContainerPackingPseudo3dLine = [ContainerPackingPseudo3dPoint, ContainerPackingPseudo3dPoint];
type ContainerPackingPseudo3dFaceGridLines = {
  front: ContainerPackingPseudo3dLine[];
  side: ContainerPackingPseudo3dLine[];
  top: ContainerPackingPseudo3dLine[];
};

export function readContainerVisualizationDimensions(
  container: ContainerPackingFormState,
): ContainerPackingVisualizationDimensions | null {
  const length = readPositiveNumberInput(container.length, 0);
  const width = readPositiveNumberInput(container.width, 0);
  const height = readPositiveNumberInput(container.height, 0);
  return length > 0 && width > 0 && height > 0 ? { length, width, height } : null;
}

export function buildPseudo3dProjection(
  viewBox: { width: number; height: number },
  dimensions: ContainerPackingVisualizationDimensions,
) {
  const origin = { x: viewBox.width - 210, y: viewBox.height - 132 };
  const xAxis = { x: -(viewBox.width - 430), y: 50 };
  const yAxis = { x: 150, y: 42 };
  const zAxis = { x: 0, y: -166 };
  const project = (x: number, y: number, z: number): ContainerPackingPseudo3dPoint => ({
    x: origin.x
      + (x / dimensions.length) * xAxis.x
      + (y / dimensions.width) * yAxis.x
      + (z / dimensions.height) * zAxis.x,
    y: origin.y
      + (x / dimensions.length) * xAxis.y
      + (y / dimensions.width) * yAxis.y
      + (z / dimensions.height) * zAxis.y,
  });

  const p000 = project(0, 0, 0);
  const p100 = project(dimensions.length, 0, 0);
  const p010 = project(0, dimensions.width, 0);
  const p110 = project(dimensions.length, dimensions.width, 0);
  const p001 = project(0, 0, dimensions.height);
  const p101 = project(dimensions.length, 0, dimensions.height);
  const p011 = project(0, dimensions.width, dimensions.height);
  const p111 = project(dimensions.length, dimensions.width, dimensions.height);

  return {
    project,
    floor: [p000, p100, p110, p010],
    backWall: [p010, p110, p111, p011],
    shellEdges: [
      [p000, p100], [p100, p110], [p110, p010], [p010, p000],
      [p001, p101], [p101, p111], [p111, p011], [p011, p001],
      [p000, p001], [p100, p101], [p110, p111], [p010, p011],
    ] as const,
    doorLabel: { x: p100.x - 18, y: p100.y + 24 },
    headLabel: { x: p000.x + 18, y: p000.y + 24 },
  };
}

export function buildPseudo3dPackedItems(
  items: ApiPackedCargoItemDto[],
  dimensions: ContainerPackingVisualizationDimensions,
  shell: ReturnType<typeof buildPseudo3dProjection>,
) {
  return mergePackedItemsForContainerRender(items, dimensions)
    .sort((left, right) =>
      left.x + left.y + left.baseHeight - (right.x + right.y + right.baseHeight))
    .map((item, index) => buildPseudo3dPackedItem(item, index, dimensions, shell));
}

function buildPseudo3dPackedItem(
  item: ContainerPackingRenderBlockSource,
  index: number,
  dimensions: ContainerPackingVisualizationDimensions,
  shell: ReturnType<typeof buildPseudo3dProjection>,
) {
  const length = clampNumber(item.length, 1, dimensions.length);
  const width = clampNumber(item.width, 1, dimensions.width);
  const height = clampNumber(item.occupiedHeight, 1, dimensions.height);
  const x = clampNumber(item.x, 0, dimensions.length - length);
  const y = clampNumber(item.y, 0, dimensions.width - width);
  const z = clampNumber(item.baseHeight, 0, dimensions.height - height);
  const x2 = x + length;
  const y2 = y + width;
  const z2 = z + height;
  const p000 = shell.project(x, y, z);
  const p100 = shell.project(x2, y, z);
  const p010 = shell.project(x, y2, z);
  const p110 = shell.project(x2, y2, z);
  const p001 = shell.project(x, y, z2);
  const p101 = shell.project(x2, y, z2);
  const p011 = shell.project(x, y2, z2);
  const p111 = shell.project(x2, y2, z2);
  const color = signedArgbToColorHex(item.colorArgb);
  const top = [p001, p101, p111, p011];
  const side = [p100, p110, p111, p101];
  const front = [p010, p011, p111, p110];

  return {
    key: `${item.name || "cargo"}-${index}`,
    color,
    frontColor: shadeHexColor(color, -6),
    sideColor: shadeHexColor(color, -16),
    topColor: shadeHexColor(color, 12),
    title: buildPackedCargoRenderBlockTitle(item),
    top,
    side,
    front,
    edgeLines: buildPseudo3dBoxEdgeLines(p000, p100, p010, p110, p001, p101, p011, p111),
    gridLines: buildPseudo3dBlockGridLines(item, [p010, p110, p111, p011], side, top),
    stackSegments: item.isPalletized ? 1 : clampContainerPackingGridSegments(item.heightSegments),
    label: item.unitsRepresented || item.loadCount ? String(item.unitsRepresented || item.loadCount) : "",
    labelPoint: {
      x: (p001.x + p101.x + p111.x + p011.x) / 4,
      y: (p001.y + p101.y + p111.y + p011.y) / 4,
    },
  };
}

function buildPackedCargoRenderBlockTitle(item: ContainerPackingRenderBlockSource) {
  return `${item.name || "货物"} / ${formatPlainNumber(item.length)} x ${formatPlainNumber(item.width)} cm / 高度 ${formatPlainNumber(item.baseHeight)} - ${formatPlainNumber(item.baseHeight + item.occupiedHeight)} cm / ${formatPlainNumber(item.unitsRepresented || item.loadCount)} 件`;
}

function buildPseudo3dBoxEdgeLines(
  p000: ContainerPackingPseudo3dPoint,
  p100: ContainerPackingPseudo3dPoint,
  p010: ContainerPackingPseudo3dPoint,
  p110: ContainerPackingPseudo3dPoint,
  p001: ContainerPackingPseudo3dPoint,
  p101: ContainerPackingPseudo3dPoint,
  p011: ContainerPackingPseudo3dPoint,
  p111: ContainerPackingPseudo3dPoint,
): ContainerPackingPseudo3dLine[] {
  void p000;
  return [
    [p110, p010],
    [p100, p110],
    [p001, p101],
    [p101, p111],
    [p111, p011],
    [p011, p001],
    [p100, p101],
    [p110, p111],
    [p010, p011],
  ];
}

function buildPseudo3dBlockGridLines(
  item: ContainerPackingRenderBlockSource,
  frontFace: ContainerPackingPseudo3dPoint[],
  sideFace: ContainerPackingPseudo3dPoint[],
  topFace: ContainerPackingPseudo3dPoint[],
): ContainerPackingPseudo3dFaceGridLines {
  if (item.isPalletized) {
    return { front: [], side: [], top: [] };
  }
  return {
    front: buildPseudo3dFaceGridLines(frontFace, item.lengthSegments, item.heightSegments),
    side: buildPseudo3dFaceGridLines(sideFace, item.widthSegments, item.heightSegments),
    top: buildPseudo3dFaceGridLines(topFace, item.lengthSegments, item.widthSegments),
  };
}

function buildPseudo3dFaceGridLines(
  face: ContainerPackingPseudo3dPoint[],
  horizontalSegments: number,
  verticalSegments: number,
) {
  if (face.length !== 4) {
    return [];
  }
  const [first, second, third, fourth] = face;
  const horizontalLines = horizontalSegments <= 1
    ? []
    : Array.from({ length: horizontalSegments - 1 }, (_, index) => {
        const ratio = (index + 1) / horizontalSegments;
        return insetPseudo3dLine([
          lerpPseudo3dPoint(first, second, ratio),
          lerpPseudo3dPoint(fourth, third, ratio),
        ]);
      });
  const verticalLines = verticalSegments <= 1
    ? []
    : Array.from({ length: verticalSegments - 1 }, (_, index) => {
        const ratio = (index + 1) / verticalSegments;
        return insetPseudo3dLine([
          lerpPseudo3dPoint(first, fourth, ratio),
          lerpPseudo3dPoint(second, third, ratio),
        ]);
      });
  return [...horizontalLines, ...verticalLines];
}

function lerpPseudo3dPoint(
  start: ContainerPackingPseudo3dPoint,
  end: ContainerPackingPseudo3dPoint,
  amount: number,
) {
  return {
    x: start.x + (end.x - start.x) * amount,
    y: start.y + (end.y - start.y) * amount,
  };
}

function insetPseudo3dLine(
  line: ContainerPackingPseudo3dLine,
  inset = 0.9,
): ContainerPackingPseudo3dLine {
  const [start, end] = line;
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const length = Math.hypot(dx, dy);
  if (length <= inset * 2) {
    return line;
  }
  const ratio = inset / length;
  return [
    { x: start.x + dx * ratio, y: start.y + dy * ratio },
    { x: end.x - dx * ratio, y: end.y - dy * ratio },
  ];
}

export function pointsToString(points: ContainerPackingPseudo3dPoint[]) {
  return points.map((point) => `${formatSvgNumber(point.x)},${formatSvgNumber(point.y)}`).join(" ");
}

export function formatSvgNumber(value: number) {
  return Number.isFinite(value) ? Number(value.toFixed(2)) : 0;
}
