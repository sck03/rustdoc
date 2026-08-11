import type { ApiPackedCargoItemDto } from "../../../api/index.ts";
import {
  clampContainerPackingGridSegments,
  clampNumber,
} from "./containerPackingRenderMath.ts";
import type {
  ContainerPackingRenderBlockSource,
  ContainerPackingVisualizationDimensions,
} from "./containerPackingVisualizationModel.ts";

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

export function mergePackedItemsForContainerRender(
  items: ApiPackedCargoItemDto[],
  dimensions: ContainerPackingVisualizationDimensions,
): ContainerPackingRenderBlockSource[] {
  const normalizedItems = items.map((item, index) => normalizePackedItemForRender(item, index, dimensions));
  const mergeable = normalizedItems.filter(canMergePackedItemForRender);
  const mergedBlocks: ContainerPackingRenderBlockSource[] = [];

  groupByKey(mergeable, createPackedItemMergeSignature).forEach((group) => {
    mergedBlocks.push(...mergeContiguousPackedSlices(mergeContiguousPackedRows(group)));
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
          : clampContainerPackingGridSegments(item.loadCount || item.unitsRepresented || 1),
      });
    });

  return mergedBlocks;
}

function normalizePackedItemForRender(
  item: ApiPackedCargoItemDto,
  index: number,
  dimensions: ContainerPackingVisualizationDimensions,
): ContainerPackingRenderBlockSource {
  const length = clampNumber(item.width, 1, dimensions.length);
  const width = clampNumber(item.height, 1, dimensions.width);
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
    baseHeight: clampNumber(item.baseHeight, 0, dimensions.height - occupiedHeight),
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
    !item.isPalletized
    && item.loadCount > 0
    && item.length > 0
    && item.width > 0
    && item.occupiedHeight > 0
  );
}

function createPackedItemMergeSignature(item: ContainerPackingRenderBlockSource) {
  const cellHeight = item.loadCount > 0 ? item.occupiedHeight / item.loadCount : item.occupiedHeight;
  return [
    item.name,
    item.colorArgb,
    item.isRotated ? "rotated" : "normal",
    item.priorityGroup,
    item.preferredZone,
    formatMergeNumber(item.length),
    formatMergeNumber(item.width),
    formatMergeNumber(item.baseHeight),
    formatMergeNumber(cellHeight),
  ].join("|");
}

function mergeContiguousPackedRows(items: ContainerPackingRenderBlockSource[]): ContainerPackingMergedRow[] {
  const rows: ContainerPackingMergedRow[] = [];
  groupByKey(
    [...items].sort(comparePackedItemForRender),
    (item) => `${formatMergeNumber(item.baseHeight)}|${formatMergeNumber(item.x)}`,
  ).forEach((group) => {
    let current: ContainerPackingMergedRow | null = null;
    group.sort((left, right) => left.y - right.y).forEach((item) => {
      if (
        current
        && areMergeNumbersClose(current.maxY, item.y)
        && areMergeNumbersClose(current.x, item.x)
        && areMergeNumbersClose(current.cellLength, item.length)
        && areMergeNumbersClose(current.occupiedHeight, item.occupiedHeight)
        && areMergeNumbersClose(current.baseHeight, item.baseHeight)
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

function mergeContiguousPackedSlices(rows: ContainerPackingMergedRow[]): ContainerPackingRenderBlockSource[] {
  const blocks: ContainerPackingRenderBlockSource[] = [];
  groupByKey(
    [...rows].sort((left, right) =>
      left.baseHeight - right.baseHeight
      || left.minY - right.minY
      || left.maxY - right.maxY
      || left.x - right.x),
    (row) => [
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
    let current: (ContainerPackingMergedRow & { minX: number; maxX: number; lengthSegments: number }) | null = null;
    group.sort((left, right) => left.x - right.x).forEach((row) => {
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
  source: ContainerPackingMergedRow & { minX: number; maxX: number; lengthSegments: number },
): ContainerPackingRenderBlockSource {
  const footprintCount = Math.max(source.lengthSegments * source.widthSegments, 1);
  const itemsPerFootprint = Math.max(Math.round(source.loadCount / footprintCount), 1);
  const cellHeight = source.loadCount > 0
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
      clampContainerPackingGridSegments(Math.round(source.occupiedHeight / Math.max(cellHeight, 1))),
      1,
    ),
  };
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

function comparePackedItemForRender(left: ContainerPackingRenderBlockSource, right: ContainerPackingRenderBlockSource) {
  return left.baseHeight - right.baseHeight || left.x - right.x || left.y - right.y;
}

function areMergeNumbersClose(left: number, right: number) {
  return Math.abs(left - right) <= 0.05;
}

function formatMergeNumber(value: number) {
  return Number.isFinite(value) ? value.toFixed(2) : "0.00";
}
