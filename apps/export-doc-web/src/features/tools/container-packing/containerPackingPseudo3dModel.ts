import type { ApiPackedCargoItemDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";
import {
  shadeHexColor,
  signedArgbToColorHex,
} from "./containerPackingModel.ts";
import { mergePackedItemsForContainerRender } from "./containerPackingRenderBlocks.ts";
import { containerPackingViewAngles } from "./containerPackingViewModel.ts";
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

export function buildPseudo3dProjection(
  viewBox: { width: number; height: number },
  dimensions: ContainerPackingVisualizationDimensions,
) {
  const { x: elevation, y: rotation } = containerPackingViewAngles.isometric;
  const yaw = -rotation;
  const raw = (x: number, y: number, z: number) => ({
    x: x * Math.cos(yaw) - y * Math.sin(yaw),
    y: (x * Math.sin(yaw) + y * Math.cos(yaw)) * Math.sin(elevation) - z * Math.cos(elevation),
  });
  const corners = [0, dimensions.length].flatMap(x => [0, dimensions.width].flatMap(y =>
    [0, dimensions.height].map(z => raw(x, y, z))));
  const minX = Math.min(...corners.map(p => p.x)), maxX = Math.max(...corners.map(p => p.x));
  const minY = Math.min(...corners.map(p => p.y)), maxY = Math.max(...corners.map(p => p.y));
  const scale = Math.min((viewBox.width - 80) / (maxX - minX), (viewBox.height - 80) / (maxY - minY));
  const project = (x: number, y: number, z: number): ContainerPackingPseudo3dPoint => {
    const point = raw(x, y, z);
    return { x: viewBox.width / 2 + (point.x - (minX + maxX) / 2) * scale,
      y: (viewBox.height - 20) / 2 + (point.y - (minY + maxY) / 2) * scale };
  };

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
    door: [p100, p110, p111, p101],
    backWall: [p000, p100, p101, p001],
    shellEdges: [
      [p000, p100], [p100, p110], [p110, p010], [p010, p000],
      [p001, p101], [p101, p111], [p111, p011], [p011, p001],
      [p000, p001], [p100, p101], [p110, p111], [p010, p011],
    ] as const,
    doorLabel: { x: p110.x, y: p110.y + 24 },
    headLabel: { x: p010.x, y: p010.y + 24 },
  };
}

export function buildPseudo3dPackedItems(
  items: ApiPackedCargoItemDto[],
  dimensions: ContainerPackingVisualizationDimensions,
  shell: ReturnType<typeof buildPseudo3dProjection>,
) {
  return mergePackedItemsForContainerRender(items, dimensions)
    .sort((left, right) => {
      const { x: elevation, y: rotation } = containerPackingViewAngles.isometric;
      const depth = (item: ContainerPackingRenderBlockSource) =>
        ((item.x + item.length / 2) * Math.sin(-rotation) + (item.y + item.width / 2) * Math.cos(rotation)) * Math.cos(elevation)
        + (item.baseHeight + item.occupiedHeight / 2) * Math.sin(elevation);
      return depth(left) - depth(right);
    })
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
    label: item.unitsRepresented || item.loadCount ? `${item.unitsRepresented || item.loadCount}件` : "",
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
