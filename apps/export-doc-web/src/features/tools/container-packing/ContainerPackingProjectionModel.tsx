import type { ApiContainerPackingAnalysisDto, ApiPackedCargoItemDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";
import { signedArgbToColorHex } from "./containerPackingModel.ts";
import type { ContainerPackingRenderBlockSource, ContainerPackingVisualizationDimensions } from "./ContainerPackingVisualization.tsx";

const maxContainerPackingGridSegments = 18;
type ContainerPackingViewKind = "top" | "side" | "door";

export function buildContainerPackingProjection(
  viewKind: ContainerPackingViewKind,
  dimensions: ContainerPackingVisualizationDimensions,
) {
  const viewWidth = 720;

  const viewHeight = 260;

  const padding = 22;

  const worldWidth = viewKind === "door" ? dimensions.width : dimensions.length;

  const worldHeight = viewKind === "top" ? dimensions.width : dimensions.height;

  const scale = Math.min(
    (viewWidth - padding * 2) / worldWidth,
    (viewHeight - padding * 2) / worldHeight,
  );

  const width = worldWidth * scale;

  const height = worldHeight * scale;

  return {
    scale,

    originX: (viewWidth - width) / 2,

    originY: (viewHeight - height) / 2,

    width,

    height,

    worldWidth,

    worldHeight,
  };
}

export function projectPackedCargoItem(
  item: ApiPackedCargoItemDto,

  viewKind: ContainerPackingViewKind,

  dimensions: ContainerPackingVisualizationDimensions,

  projection: ReturnType<typeof buildContainerPackingProjection>,
) {
  const footprint = readPackedCargoFootprint(item);

  const worldRect =
    viewKind === "top"
      ? {
          x: item.x,
          y: item.y,
          width: footprint.length,
          height: footprint.width,
        }
      : viewKind === "side"
        ? {
            x: item.x,

            y: dimensions.height - item.topHeight,

            width: footprint.length,

            height: item.occupiedHeight,
          }
        : {
            x: item.y,

            y: dimensions.height - item.topHeight,

            width: footprint.width,

            height: item.occupiedHeight,
          };

  const rect = clampWorldRect(
    worldRect,
    projection.worldWidth,
    projection.worldHeight,
  );

  const screenX =
    viewKind === "top" || viewKind === "side"
      ? projection.originX +
        (projection.worldWidth - rect.x - rect.width) * projection.scale
      : projection.originX + rect.x * projection.scale;

  return {
    x: screenX,

    y: projection.originY + rect.y * projection.scale,

    width: Math.max(rect.width * projection.scale, 1),

    height: Math.max(rect.height * projection.scale, 1),
  };
}

export function renderContainerPackingItemGrid(
  item: ApiPackedCargoItemDto,

  viewKind: ContainerPackingViewKind,

  rect: { x: number; y: number; width: number; height: number },
) {
  if (item.isPalletized || rect.width < 12 || rect.height < 12) {
    return null;
  }

  const segments = estimateContainerPackingGridSegments(item, viewKind, rect);

  const lines = [
    ...buildContainerPackingGridLines("x", rect, segments.xSegments),

    ...buildContainerPackingGridLines("y", rect, segments.ySegments),
  ];

  if (lines.length === 0) {
    return null;
  }

  return (
    <g className="container-packing-item-grid" aria-hidden="true">
      {lines.map((line, index) => (
        <line
          key={`${line.axis}-${index}`}


          className="container-packing-item-grid-line"

          x1={line.x1}

          y1={line.y1}

          x2={line.x2}

          y2={line.y2}
        />
      ))}
    </g>
  );
}

function estimateContainerPackingGridSegments(
  item: ApiPackedCargoItemDto,

  viewKind: ContainerPackingViewKind,

  rect: { width: number; height: number },
) {
  const loadCount = Math.max(
    Math.trunc(item.loadCount || item.unitsRepresented || 1),
    1,
  );

  const boundedCount = Math.min(
    loadCount,
    maxContainerPackingGridSegments * maxContainerPackingGridSegments,
  );

  const aspect = Math.max(rect.width / Math.max(rect.height, 1), 0.25);

  const footprintXSegments = clampContainerPackingGridSegments(
    Math.ceil(Math.sqrt(boundedCount * aspect)),
  );

  const footprintYSegments = clampContainerPackingGridSegments(
    Math.ceil(boundedCount / footprintXSegments),
  );

  if (viewKind === "top") {
    return {
      xSegments: footprintXSegments,

      ySegments: footprintYSegments,
    };
  }

  return {
    xSegments: viewKind === "side" ? footprintXSegments : footprintYSegments,

    ySegments: clampContainerPackingGridSegments(loadCount),
  };
}

function buildContainerPackingGridLines(
  axis: "x" | "y",

  rect: { x: number; y: number; width: number; height: number },

  segments: number,
) {
  if (segments <= 1) {
    return [];
  }

  return Array.from({ length: segments - 1 }, (_, index) => {
    const offset = (index + 1) / segments;

    if (axis === "x") {
      const x = rect.x + rect.width * offset;

      return { axis, x1: x, y1: rect.y, x2: x, y2: rect.y + rect.height };
    }

    const y = rect.y + rect.height * offset;

    return { axis, x1: rect.x, y1: y, x2: rect.x + rect.width, y2: y };
  });
}

function clampContainerPackingGridSegments(value: number) {
  if (!Number.isFinite(value)) {
    return 1;
  }

  return Math.min(
    Math.max(Math.trunc(value), 1),
    maxContainerPackingGridSegments,
  );
}

function clampWorldRect(
  rect: { x: number; y: number; width: number; height: number },

  worldWidth: number,

  worldHeight: number,
) {
  const x = clampNumber(rect.x, 0, worldWidth);

  const y = clampNumber(rect.y, 0, worldHeight);

  const width = clampNumber(rect.width, 0, worldWidth - x);

  const height = clampNumber(rect.height, 0, worldHeight - y);

  return { x, y, width, height };
}

export function renderContainerPackingGuides(
  viewKind: ContainerPackingViewKind,

  projection: ReturnType<typeof buildContainerPackingProjection>,
) {
  if (viewKind !== "top") {
    return (
      <>
        <line
          className="container-packing-guide-line"

          x1={projection.originX}

          y1={projection.originY + projection.height}

          x2={projection.originX + projection.width}

          y2={projection.originY + projection.height}
        />

        <text
          className="container-packing-axis-label"
          x={projection.originX + 4}
          y={projection.originY + projection.height - 6}
        >
          地板
        </text>
      </>
    );
  }

  const firstZoneX = projection.originX + projection.width / 3;

  const secondZoneX = projection.originX + (projection.width * 2) / 3;

  return (
    <>
      {[firstZoneX, secondZoneX].map((x) => (
        <line
          key={x}

          className="container-packing-guide-line"

          x1={x}

          y1={projection.originY}

          x2={x}

          y2={projection.originY + projection.height}
        />
      ))}

      <text
        className="container-packing-axis-label"
        x={projection.originX + 4}
        y={projection.originY - 7}
      >
        柜门
      </text>

      <text
        className="container-packing-axis-label"

        x={projection.originX + projection.width - 4}

        y={projection.originY - 7}

        textAnchor="end"
      >
        柜头
      </text>
    </>
  );
}

export function renderContainerPackingCenterOfGravity(
  analysis: ApiContainerPackingAnalysisDto,

  dimensions: ContainerPackingVisualizationDimensions,

  projection: ReturnType<typeof buildContainerPackingProjection>,
) {
  if (
    !Number.isFinite(analysis.centerOfGravityX) ||
    !Number.isFinite(analysis.centerOfGravityY)
  ) {
    return null;
  }

  const x =
    projection.originX +
    (dimensions.length -
      clampNumber(analysis.centerOfGravityX, 0, dimensions.length)) *
      projection.scale;

  const y =
    projection.originY +
    clampNumber(analysis.centerOfGravityY, 0, dimensions.width) *
      projection.scale;

  return (
    <g
      className={
        analysis.isCenterOfGravityWithinTolerance
          ? "container-packing-cog"
          : "container-packing-cog warning"
      }
    >
      <line x1={x - 8} y1={y} x2={x + 8} y2={y} />

      <line x1={x} y1={y - 8} x2={x} y2={y + 8} />

      <circle cx={x} cy={y} r="4">
        <title>
          重心 {formatPlainNumber(analysis.centerOfGravityX)},{" "}
          {formatPlainNumber(analysis.centerOfGravityY)}
        </title>
      </circle>
    </g>
  );
}

export function buildContainerPackingLegend(items: ApiPackedCargoItemDto[]) {
  const map = new Map<string, { key: string; name: string; color: string }>();

  items.forEach((item) => {
    const name = item.name || "货物";

    const color = signedArgbToColorHex(item.colorArgb);

    const key = `${name}-${color}`;

    if (!map.has(key)) {
      map.set(key, { key, name, color });
    }
  });

  return Array.from(map.values());
}

export function buildPackedCargoTitle(item: ApiPackedCargoItemDto) {
  const footprint = readPackedCargoFootprint(item);

  const footprintText = `${formatPlainNumber(footprint.length)} x ${formatPlainNumber(footprint.width)} cm`;

  const heightText = `${formatPlainNumber(item.baseHeight)} - ${formatPlainNumber(item.topHeight)} cm`;

  const countText = `${formatPlainNumber(item.unitsRepresented || item.loadCount)} 件`;

  return `${item.name || "货物"} / ${footprintText} / 高度 ${heightText} / ${countText}`;
}

function buildPackedCargoRenderBlockTitle(
  item: ContainerPackingRenderBlockSource,
) {
  const footprintText = `${formatPlainNumber(item.length)} x ${formatPlainNumber(item.width)} cm`;

  const heightText = `${formatPlainNumber(item.baseHeight)} - ${formatPlainNumber(item.baseHeight + item.occupiedHeight)} cm`;

  const countText = `${formatPlainNumber(item.unitsRepresented || item.loadCount)} 件`;

  return `${item.name || "货物"} / ${footprintText} / 高度 ${heightText} / ${countText}`;
}

function readPackedCargoFootprint(item: ApiPackedCargoItemDto) {
  return {
    length: item.width,

    width: item.height,
  };
}

function clampNumber(value: number, min: number, max: number) {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(Math.max(value, min), max);
}

