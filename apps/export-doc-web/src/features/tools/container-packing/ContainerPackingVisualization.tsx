import { lazy, Suspense, useId } from "react";
import type { ApiContainerPackingAnalysisDto, ApiPackedCargoItemDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";
import type { ContainerPackingRenderModeValue } from "./containerPackingModel.ts";
import { signedArgbToColorHex, shadeHexColor } from "./containerPackingModel.ts";
import {
 buildPseudo3dPackedItems, buildPseudo3dProjection, formatSvgNumber, pointsToString,
} from "./containerPackingPseudo3dModel.ts";
import { buildContainerPackingLegend, buildContainerPackingProjection, buildPackedCargoTitle, projectPackedCargoItem, renderContainerPackingCenterOfGravity, renderContainerPackingGuides, renderContainerPackingItemGrid } from "./ContainerPackingProjectionModel.tsx";

const ContainerPackingScene3d = lazy(() => import("./ContainerPackingScene3d.tsx"));

export function ContainerPackingVisualization({
  analysis,

  dimensions,

  renderMode,
}: {
  analysis: ApiContainerPackingAnalysisDto;

  dimensions: ContainerPackingVisualizationDimensions;

  renderMode: ContainerPackingRenderModeValue;
}) {
  const legendItems = buildContainerPackingLegend(analysis.packedItems);

  return (
    <section
      className="container-packing-visualization"
      aria-label="装柜平面可视化"
    >
      <div className="container-packing-visual-header">
        <div>
          <strong>装柜视图</strong>

          <span>
            {formatPlainNumber(dimensions.length)} x{" "}
            {formatPlainNumber(dimensions.width)} x{" "}
            {formatPlainNumber(dimensions.height)} cm
          </span>
        </div>

        <div
          className={
            analysis.isCenterOfGravityWithinTolerance
              ? "status-pill status-pill-ok"
              : "status-pill status-pill-warning"
          }
        >
          {analysis.isCenterOfGravityWithinTolerance ? "重心正常" : "重心超限"}
        </div>
      </div>

      <ContainerPackingPseudo3dView
        dimensions={dimensions}
        analysis={analysis}
        renderMode={renderMode}
      />

      <div
        className="container-packing-visual-grid"
        aria-label="装柜正投影视图"
      >
        <ContainerPackingProjectionView
          title="俯视图"
          subtitle="柜门 / 柜头"
          viewKind="top"
          dimensions={dimensions}
          analysis={analysis}
          renderMode={renderMode}
        />

        <ContainerPackingProjectionView
          title="侧视图"
          subtitle="长向 / 高向"
          viewKind="side"
          dimensions={dimensions}
          analysis={analysis}
          renderMode={renderMode}
        />

        <ContainerPackingProjectionView
          title="柜门视图"
          subtitle="宽向 / 高向"
          viewKind="door"
          dimensions={dimensions}
          analysis={analysis}
          renderMode={renderMode}
        />
      </div>

      {legendItems.length > 0 ? (
        <div className="container-packing-legend" aria-label="装柜颜色图例">
          {legendItems.map((item) => (
            <span key={item.key} className="container-packing-legend-item">
              <span
                className="container-packing-legend-swatch"
                style={{ backgroundColor: item.color }}
                aria-hidden="true"
              />

              <span>{item.name}</span>
            </span>
          ))}
        </div>
      ) : null}
    </section>
  );
}

export type ContainerPackingVisualizationDimensions = {
  length: number;

  width: number;

  height: number;
};

type ContainerPackingViewKind = "top" | "side" | "door";

type ContainerPackingPseudo3dPoint = {
  x: number;

  y: number;
};

type ContainerPackingPseudo3dLine = [
  ContainerPackingPseudo3dPoint,
  ContainerPackingPseudo3dPoint,
];

type ContainerPackingPseudo3dFaceGridLines = {
  front: ContainerPackingPseudo3dLine[];

  side: ContainerPackingPseudo3dLine[];

  top: ContainerPackingPseudo3dLine[];
};

export type ContainerPackingRenderBlockSource = {
  key: string;

  name: string;

  colorArgb: number;

  isRotated: boolean;

  isPalletized: boolean;

  x: number;

  y: number;

  length: number;

  width: number;

  baseHeight: number;

  occupiedHeight: number;

  unitsRepresented: number;

  loadCount: number;

  totalWeight: number;

  priorityGroup: string;

  preferredZone: string;

  lengthSegments: number;

  widthSegments: number;

  heightSegments: number;
};

function ContainerPackingPseudo3dView({
  dimensions,

  analysis,

  renderMode,
}: {
  dimensions: ContainerPackingVisualizationDimensions;

  analysis: ApiContainerPackingAnalysisDto;

  renderMode: ContainerPackingRenderModeValue;
}) {
  const clipIdPrefix = `container-packing-pseudo3d-${useId().replace(/:/g, "")}`;

  const viewBox = { width: 980, height: 360 };

  const shell = buildPseudo3dProjection(viewBox, dimensions);

  const items = buildPseudo3dPackedItems(
    analysis.packedItems,
    dimensions,
    shell,
  );

  return (
    <svg
      className="container-packing-pseudo3d-svg"
      viewBox={`0 0 ${viewBox.width} ${viewBox.height}`}
      role="img"
      aria-label="装柜效果图"
    >
      <polygon
        className="container-packing-pseudo3d-floor"
        points={pointsToString(shell.floor)}
      />

      <polygon
        className="container-packing-pseudo3d-wall"
        points={pointsToString(shell.backWall)}
      />

      <g className="container-packing-pseudo3d-shell" aria-hidden="true">
        {shell.shellEdges.map((edge, index) => (
          <line
            key={`shell-edge-${index}`}

            x1={formatSvgNumber(edge[0].x)}

            y1={formatSvgNumber(edge[0].y)}

            x2={formatSvgNumber(edge[1].x)}

            y2={formatSvgNumber(edge[1].y)}

            data-shell-edge={index === 1 ? "door-width-bottom" : undefined}
          />
        ))}
      </g>

      <text
        className="container-packing-axis-label"
        x={shell.doorLabel.x}
        y={shell.doorLabel.y}
        textAnchor="middle"
      >
        柜门
      </text>

      <text
        className="container-packing-axis-label"
        x={shell.headLabel.x}
        y={shell.headLabel.y}
        textAnchor="middle"
      >
        柜头
      </text>

      {items.length === 0 ? (
        <text
          className="container-packing-empty-label"
          x={viewBox.width / 2}
          y={viewBox.height / 2}
          textAnchor="middle"
        >
          暂无装载结果
        </text>
      ) : null}

      {items.map((item, index) => {
        const itemClipId = `${clipIdPrefix}-${index}`;

        return (
          <g
            key={`${item.key}-${index}`}
            className="container-packing-pseudo3d-item"
            data-load-count={item.stackSegments}
          >
            <defs>
              <clipPath
                id={`${itemClipId}-side`}
                clipPathUnits="userSpaceOnUse"
              >
                <polygon points={pointsToString(item.side)} />
              </clipPath>

              <clipPath
                id={`${itemClipId}-front`}
                clipPathUnits="userSpaceOnUse"
              >
                <polygon points={pointsToString(item.front)} />
              </clipPath>

              <clipPath id={`${itemClipId}-top`} clipPathUnits="userSpaceOnUse">
                <polygon points={pointsToString(item.top)} />
              </clipPath>
            </defs>

            <polygon
              data-face="length-side"
              points={pointsToString(item.side)}
              fill={item.sideColor}
            >
              <title>{item.title}</title>
            </polygon>

            <polygon
              data-face="width-side"
              points={pointsToString(item.front)}
              fill={item.frontColor}
            />

            <polygon
              data-face="top"
              points={pointsToString(item.top)}
              fill={item.topColor}
            />

            {renderMode === "FullGrid" ? (
              <>
                {renderPseudo3dGridLines(
                  item.gridLines.side,
                  `${itemClipId}-side`,
                  "side",
                )}

                {renderPseudo3dGridLines(
                  item.gridLines.front,
                  `${itemClipId}-front`,
                  "front",
                )}

                {renderPseudo3dGridLines(
                  item.gridLines.top,
                  `${itemClipId}-top`,
                  "top",
                )}
              </>
            ) : null}

            {item.edgeLines.map((line, lineIndex) => (
              <line
                key={`edge-${lineIndex}`}

                className="container-packing-pseudo3d-item-edge"

                x1={formatSvgNumber(line[0].x)}

                y1={formatSvgNumber(line[0].y)}

                x2={formatSvgNumber(line[1].x)}

                y2={formatSvgNumber(line[1].y)}
              />
            ))}

            {renderMode === "FullGrid" && item.label ? (
              <text
                className="container-packing-item-label"
                x={item.labelPoint.x}
                y={item.labelPoint.y}
                textAnchor="middle"
                dominantBaseline="central"
              >
                {item.label}
              </text>
            ) : null}
          </g>
        );
      })}
    </svg>
  );
}

function renderPseudo3dGridLines(
  lines: ContainerPackingPseudo3dLine[],
  clipPathId: string,
  faceName: string,
) {
  if (lines.length === 0) {
    return null;
  }

  return (
    <g
      key={`grid-${faceName}`}
      clipPath={`url(#${clipPathId})`}
      data-grid-face={faceName}
    >
      {lines.map((line, lineIndex) => (
        <line
          key={`grid-${faceName}-${lineIndex}`}

          className="container-packing-pseudo3d-item-grid-line"

          x1={formatSvgNumber(line[0].x)}

          y1={formatSvgNumber(line[0].y)}

          x2={formatSvgNumber(line[1].x)}

          y2={formatSvgNumber(line[1].y)}
        />
      ))}
    </g>
  );
}

function ContainerPackingProjectionView({
  title,

  subtitle,

  viewKind,

  dimensions,

  analysis,

  renderMode,
}: {
  title: string;

  subtitle: string;

  viewKind: ContainerPackingViewKind;

  dimensions: ContainerPackingVisualizationDimensions;

  analysis: ApiContainerPackingAnalysisDto;

  renderMode: ContainerPackingRenderModeValue;
}) {
  const projection = buildContainerPackingProjection(viewKind, dimensions);

  return (
    <div
      className="container-packing-projection-view"
      data-view-kind={viewKind}
    >
      <div className="container-packing-projection-header">
        <strong>{title}</strong>

        <span>{subtitle}</span>
      </div>

      <svg
        className="container-packing-svg"
        viewBox="0 0 720 260"
        role="img"
        aria-label={title}
      >
        <rect
          className="container-packing-shell"

          x={projection.originX}

          y={projection.originY}

          width={projection.width}

          height={projection.height}

          rx="6"
        />

        {renderContainerPackingGuides(viewKind, projection)}

        {analysis.packedItems.map((item, index) => {
          const rect = projectPackedCargoItem(
            item,
            viewKind,
            dimensions,
            projection,
          );

          const color = signedArgbToColorHex(item.colorArgb);

          const label = item.loadCount > 1 ? String(item.loadCount) : "";

          return (
            <g key={`${viewKind}-${item.name}-${index}`}>
              <rect
                className="container-packing-item-rect"

                x={rect.x}

                y={rect.y}

                width={rect.width}

                height={rect.height}

                rx="3"

                fill={color}
              >
                <title>{buildPackedCargoTitle(item)}</title>
              </rect>

              {renderMode === "FullGrid"
                ? renderContainerPackingItemGrid(item, viewKind, rect)
                : null}

              {rect.width >= 24 && rect.height >= 18 && label ? (
                <text
                  className="container-packing-item-label"

                  x={rect.x + rect.width / 2}

                  y={rect.y + rect.height / 2}

                  textAnchor="middle"

                  dominantBaseline="central"
                >
                  {label}
                </text>
              ) : null}
            </g>
          );
        })}

        {viewKind === "top"
          ? renderContainerPackingCenterOfGravity(
              analysis,
              dimensions,
              projection,
            )
          : null}
      </svg>
    </div>
  );
}

