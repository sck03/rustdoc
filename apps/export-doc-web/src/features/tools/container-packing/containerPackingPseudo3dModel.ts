import type { ApiPackedCargoItemDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";
import type { ContainerPackingFormState } from "./containerPackingModel.ts";
import { readPositiveNumberInput, shadeHexColor, signedArgbToColorHex } from "./containerPackingModel.ts";
import type { ContainerPackingRenderBlockSource, ContainerPackingVisualizationDimensions } from "./ContainerPackingVisualization.tsx";

type ContainerPackingPseudo3dPoint = { x: number; y: number };
export type ContainerPackingPseudo3dLine = [ContainerPackingPseudo3dPoint, ContainerPackingPseudo3dPoint];
type ContainerPackingPseudo3dFaceGridLines = { front: ContainerPackingPseudo3dLine[]; side: ContainerPackingPseudo3dLine[]; top: ContainerPackingPseudo3dLine[] };

const maxContainerPackingGridSegments = 18;

function clampContainerPackingGridSegments(value: number) {
  return Number.isFinite(value) ? Math.min(Math.max(Math.trunc(value), 1), maxContainerPackingGridSegments) : 1;
}

function readPackedCargoFootprint(item: ApiPackedCargoItemDto) {
  return { length: item.width, width: item.height };
}

function buildPackedCargoRenderBlockTitle(item: ContainerPackingRenderBlockSource) {
  return `${item.name || "货物"} / ${formatPlainNumber(item.length)} x ${formatPlainNumber(item.width)} cm / 高度 ${formatPlainNumber(item.baseHeight)} - ${formatPlainNumber(item.baseHeight + item.occupiedHeight)} cm / ${formatPlainNumber(item.unitsRepresented || item.loadCount)} 件`;
}

export function clampNumber(value: number, min: number, max: number) {
  return Number.isFinite(value) ? Math.min(Math.max(value, min), max) : min;
}

export function readContainerVisualizationDimensions(
  container: ContainerPackingFormState,
): ContainerPackingVisualizationDimensions | null {
  const length = readPositiveNumberInput(container.length, 0);

  const width = readPositiveNumberInput(container.width, 0);

  const height = readPositiveNumberInput(container.height, 0);

  return length > 0 && width > 0 && height > 0
    ? {
        length,

        width,

        height,
      }
    : null;
}

export function buildPseudo3dProjection(
  viewBox: { width: number; height: number },
  dimensions: ContainerPackingVisualizationDimensions,
) {
  const origin = { x: viewBox.width - 210, y: viewBox.height - 132 };

  const xAxis = { x: -(viewBox.width - 430), y: 50 };

  const yAxis = { x: 150, y: 42 };

  const zAxis = { x: 0, y: -166 };

  const project = (
    x: number,
    y: number,
    z: number,
  ): ContainerPackingPseudo3dPoint => ({
    x:
      origin.x +
      (x / dimensions.length) * xAxis.x +
      (y / dimensions.width) * yAxis.x +
      (z / dimensions.height) * zAxis.x,

    y:
      origin.y +
      (x / dimensions.length) * xAxis.y +
      (y / dimensions.width) * yAxis.y +
      (z / dimensions.height) * zAxis.y,
  });

  const p000 = project(0, 0, 0);

  const p100 = project(dimensions.length, 0, 0);

  const p010 = project(0, dimensions.width, 0);

  const p110 = project(dimensions.length, dimensions.width, 0);

  const p001 = project(0, 0, dimensions.height);

  const p101 = project(dimensions.length, 0, dimensions.height);

  const p011 = project(0, dimensions.width, dimensions.height);

  const p111 = project(dimensions.length, dimensions.width, dimensions.height);

  const floor = [p000, p100, p110, p010];

  const backWall = [p010, p110, p111, p011];

  const shellEdges = [
    [p000, p100],

    [p100, p110],

    [p110, p010],

    [p010, p000],

    [p001, p101],

    [p101, p111],

    [p111, p011],

    [p011, p001],

    [p000, p001],

    [p100, p101],

    [p110, p111],

    [p010, p011],
  ] as const;

  return {
    project,

    floor,

    backWall,

    shellEdges,

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
    .sort(
      (left, right) =>
        left.x +
        left.y +
        left.baseHeight -
        (right.x + right.y + right.baseHeight),
    )

    .map((item, index) => {
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

      const label =
        item.unitsRepresented || item.loadCount
          ? String(item.unitsRepresented || item.loadCount)
          : "";

      const color = signedArgbToColorHex(item.colorArgb);

      const stackSegments = item.isPalletized
        ? 1
        : clampContainerPackingGridSegments(item.heightSegments);

      const top = [p001, p101, p111, p011];

      const side = [p100, p110, p111, p101];

      const front = [p010, p011, p111, p110];

      const frontGridFace = [p010, p110, p111, p011];

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

        edgeLines: buildPseudo3dBoxEdgeLines(
          p000,
          p100,
          p010,
          p110,
          p001,
          p101,
          p011,
          p111,
        ),

        gridLines: buildPseudo3dBlockGridLines(item, frontGridFace, side, top),

        stackSegments,

        label,

        labelPoint: {
          x: (p001.x + p101.x + p111.x + p011.x) / 4,

          y: (p001.y + p101.y + p111.y + p011.y) / 4,
        },
      };
    });
}

function mergePackedItemsForContainerRender(
  items: ApiPackedCargoItemDto[],

  dimensions: ContainerPackingVisualizationDimensions,
): ContainerPackingRenderBlockSource[] {
  const normalizedItems = items.map((item, index) =>
    normalizePackedItemForRender(item, index, dimensions),
  );

  const mergeable = normalizedItems.filter(canMergePackedItemForRender);

  const mergedBlocks: ContainerPackingRenderBlockSource[] = [];

  groupByKey(mergeable, createPackedItemMergeSignature).forEach((group) => {
    const rows = mergeContiguousPackedRows(group);

    mergedBlocks.push(...mergeContiguousPackedSlices(rows));
  });

  normalizedItems

    .filter((item) => !canMergePackedItemForRender(item))

    .forEach((item) => {
      mergedBlocks.push({
        ...item,

        lengthSegments: 1,

        widthSegments: 1,

        heightSegments: item.isPalletized
          ? 1
          : clampContainerPackingGridSegments(
              item.loadCount || item.unitsRepresented || 1,
            ),
      });
    });

  return mergedBlocks;
}

function normalizePackedItemForRender(
  item: ApiPackedCargoItemDto,

  index: number,

  dimensions: ContainerPackingVisualizationDimensions,
): ContainerPackingRenderBlockSource {
  const footprint = readPackedCargoFootprint(item);

  const length = clampNumber(footprint.length, 1, dimensions.length);

  const width = clampNumber(footprint.width, 1, dimensions.width);

  const occupiedHeight = clampNumber(
    item.occupiedHeight || item.topHeight - item.baseHeight,
    1,
    dimensions.height,
  );

  return {
    key: `${item.name || "cargo"}-${index}`,

    name: item.name || `货物 ${index + 1}`,

    colorArgb: item.colorArgb,

    isRotated: item.isRotated,

    isPalletized: item.isPalletized,

    x: clampNumber(item.x, 0, dimensions.length - length),

    y: clampNumber(item.y, 0, dimensions.width - width),

    length,

    width,

    baseHeight: clampNumber(
      item.baseHeight,
      0,
      dimensions.height - occupiedHeight,
    ),

    occupiedHeight,

    unitsRepresented: Math.max(Math.trunc(item.unitsRepresented || 0), 0),

    loadCount: Math.max(Math.trunc(item.loadCount || 0), 0),

    totalWeight: item.totalWeight || 0,

    priorityGroup: item.priorityGroup || "",

    preferredZone: item.preferredZone || "",

    lengthSegments: 1,

    widthSegments: 1,

    heightSegments: 1,
  };
}

function canMergePackedItemForRender(item: ContainerPackingRenderBlockSource) {
  return (
    !item.isPalletized &&
    item.loadCount > 0 &&
    item.length > 0 &&
    item.width > 0 &&
    item.occupiedHeight > 0
  );
}

function createPackedItemMergeSignature(
  item: ContainerPackingRenderBlockSource,
) {
  const cellHeight =
    item.loadCount > 0
      ? item.occupiedHeight / item.loadCount
      : item.occupiedHeight;

  return [
    item.name,

    item.colorArgb,

    item.isRotated ? "rotated" : "normal",

    formatMergeNumber(item.length),

    formatMergeNumber(item.width),

    formatMergeNumber(item.baseHeight),

    formatMergeNumber(cellHeight),
  ].join("|");
}

type ContainerPackingMergedRow = {
  x: number;

  minY: number;

  maxY: number;

  cellLength: number;

  cellWidth: number;

  baseHeight: number;

  occupiedHeight: number;

  colorArgb: number;

  isRotated: boolean;

  name: string;

  priorityGroup: string;

  preferredZone: string;

  unitsRepresented: number;

  loadCount: number;

  totalWeight: number;

  widthSegments: number;
};

function mergeContiguousPackedRows(
  items: ContainerPackingRenderBlockSource[],
): ContainerPackingMergedRow[] {
  const rows: ContainerPackingMergedRow[] = [];

  groupByKey(
    [...items].sort(comparePackedItemForRender),

    (item) =>
      `${formatMergeNumber(item.baseHeight)}|${formatMergeNumber(item.x)}`,
  ).forEach((group) => {
    let current: ContainerPackingMergedRow | null = null;

    group

      .sort((left, right) => left.y - right.y)

      .forEach((item) => {
        if (
          current &&
          areMergeNumbersClose(current.maxY, item.y) &&
          areMergeNumbersClose(current.x, item.x) &&
          areMergeNumbersClose(current.cellLength, item.length) &&
          areMergeNumbersClose(current.occupiedHeight, item.occupiedHeight) &&
          areMergeNumbersClose(current.baseHeight, item.baseHeight)
        ) {
          current = {
            ...current,

            maxY: item.y + item.width,

            unitsRepresented: current.unitsRepresented + item.unitsRepresented,

            loadCount: current.loadCount + item.loadCount,

            totalWeight: current.totalWeight + item.totalWeight,

            widthSegments: current.widthSegments + 1,
          };

          return;
        }

        if (current) {
          rows.push(current);
        }

        current = {
          x: item.x,

          minY: item.y,

          maxY: item.y + item.width,

          cellLength: item.length,

          cellWidth: item.width,

          baseHeight: item.baseHeight,

          occupiedHeight: item.occupiedHeight,

          colorArgb: item.colorArgb,

          isRotated: item.isRotated,

          name: item.name,

          priorityGroup: item.priorityGroup,

          preferredZone: item.preferredZone,

          unitsRepresented: item.unitsRepresented,

          loadCount: item.loadCount,

          totalWeight: item.totalWeight,

          widthSegments: 1,
        };
      });

    if (current) {
      rows.push(current);
    }
  });

  return rows;
}

function mergeContiguousPackedSlices(
  rows: ContainerPackingMergedRow[],
): ContainerPackingRenderBlockSource[] {
  const blocks: ContainerPackingRenderBlockSource[] = [];

  groupByKey(
    [...rows].sort(
      (left, right) =>
        left.baseHeight - right.baseHeight ||
        left.minY - right.minY ||
        left.maxY - right.maxY ||
        left.x - right.x,
    ),

    (row) =>
      [
        formatMergeNumber(row.baseHeight),

        formatMergeNumber(row.minY),

        formatMergeNumber(row.maxY),

        formatMergeNumber(row.occupiedHeight),

        row.colorArgb,

        row.isRotated ? "rotated" : "normal",

        row.name,

        row.priorityGroup,

        row.preferredZone,

        formatMergeNumber(row.cellLength),

        formatMergeNumber(row.cellWidth),
      ].join("|"),
  ).forEach((group) => {
    let current:
      | (ContainerPackingMergedRow & {
          minX: number;
          maxX: number;
          lengthSegments: number;
        })
      | null = null;

    group

      .sort((left, right) => left.x - right.x)

      .forEach((row) => {
        if (current && areMergeNumbersClose(current.maxX, row.x)) {
          current = {
            ...current,

            maxX: row.x + row.cellLength,

            unitsRepresented: current.unitsRepresented + row.unitsRepresented,

            loadCount: current.loadCount + row.loadCount,

            totalWeight: current.totalWeight + row.totalWeight,

            lengthSegments: current.lengthSegments + 1,
          };

          return;
        }

        if (current) {
          blocks.push(createMergedRenderBlockSource(current));
        }

        current = {
          ...row,

          minX: row.x,

          maxX: row.x + row.cellLength,

          lengthSegments: 1,
        };
      });

    if (current) {
      blocks.push(createMergedRenderBlockSource(current));
    }
  });

  return blocks;
}

function createMergedRenderBlockSource(
  source: ContainerPackingMergedRow & {
    minX: number;
    maxX: number;
    lengthSegments: number;
  },
): ContainerPackingRenderBlockSource {
  const itemsPerFootprint = Math.max(
    Math.round(
      source.loadCount /
        Math.max(source.lengthSegments * source.widthSegments, 1),
    ),
    1,
  );

  const cellHeight =
    source.loadCount > 0
      ? source.occupiedHeight / itemsPerFootprint
      : source.occupiedHeight;

  return {
    key: `${source.name}-${source.minX}-${source.minY}-${source.baseHeight}`,

    name: source.name,

    colorArgb: source.colorArgb,

    isRotated: source.isRotated,

    isPalletized: false,

    x: source.minX,

    y: source.minY,

    length: source.maxX - source.minX,

    width: source.maxY - source.minY,

    baseHeight: source.baseHeight,

    occupiedHeight: source.occupiedHeight,

    unitsRepresented: source.unitsRepresented,

    loadCount: source.loadCount,

    totalWeight: source.totalWeight,

    priorityGroup: source.priorityGroup,

    preferredZone: source.preferredZone,

    lengthSegments: Math.max(source.lengthSegments, 1),

    widthSegments: Math.max(source.widthSegments, 1),

    heightSegments: Math.max(
      clampContainerPackingGridSegments(
        Math.round(source.occupiedHeight / Math.max(cellHeight, 1)),
      ),
      1,
    ),
  };
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
    return {
      front: [],

      side: [],

      top: [],
    };
  }

  return {
    front: buildPseudo3dFaceGridLines(
      frontFace,
      item.lengthSegments,
      item.heightSegments,
    ),

    side: buildPseudo3dFaceGridLines(
      sideFace,
      item.widthSegments,
      item.heightSegments,
    ),

    top: buildPseudo3dTopGridLines(
      topFace,
      item.lengthSegments,
      item.widthSegments,
    ),
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

  const horizontalLines =
    horizontalSegments <= 1
      ? []
      : Array.from({ length: horizontalSegments - 1 }, (_, index) => {
          const ratio = (index + 1) / horizontalSegments;

          return insetPseudo3dLine([
            lerpPseudo3dPoint(first, second, ratio),
            lerpPseudo3dPoint(fourth, third, ratio),
          ]);
        });

  const verticalLines =
    verticalSegments <= 1
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

function buildPseudo3dTopGridLines(
  face: ContainerPackingPseudo3dPoint[],
  lengthSegments: number,
  widthSegments: number,
) {
  return buildPseudo3dFaceGridLines(face, lengthSegments, widthSegments);
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

function groupByKey<T>(items: T[], keySelector: (item: T) => string) {
  const groups = new Map<string, T[]>();

  items.forEach((item) => {
    const key = keySelector(item);

    const group = groups.get(key);

    if (group) {
      group.push(item);
    } else {
      groups.set(key, [item]);
    }
  });

  return groups;
}

function comparePackedItemForRender(
  left: ContainerPackingRenderBlockSource,
  right: ContainerPackingRenderBlockSource,
) {
  return (
    left.baseHeight - right.baseHeight || left.x - right.x || left.y - right.y
  );
}

function areMergeNumbersClose(left: number, right: number) {
  return Math.abs(left - right) <= 0.05;
}

function formatMergeNumber(value: number) {
  return Number.isFinite(value) ? value.toFixed(2) : "0.00";
}

export function pointsToString(points: ContainerPackingPseudo3dPoint[]) {
  return points
    .map((point) => `${formatSvgNumber(point.x)},${formatSvgNumber(point.y)}`)
    .join(" ");
}

export function formatSvgNumber(value: number) {
  return Number.isFinite(value) ? Number(value.toFixed(2)) : 0;
}

