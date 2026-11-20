import type {
  ApiContainerPackingAnalysisDto,
  ApiContainerTypeDto,
} from "../../../api/index.ts";
import { ExactDecimal } from "../../../ui/decimalSum.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";

export type ContainerPackingZoneValue = "Auto" | "Head" | "Middle" | "Door";

export type ContainerPackingRenderModeValue = "OutlineOnly" | "FullGrid";

export type ContainerPackingAnalyzeMode = "manual" | "auto";

export type ContainerPackingCargoRow = {
  id: string;

  name: string;

  length: string;

  width: string;

  height: string;

  weight: string;

  quantity: string;

  colorHex: string;

  usePallet: boolean;

  unitsPerPallet: string;

  maxTopLoadWeight: string;

  preferredZone: ContainerPackingZoneValue;

  loadSequence: string;

  priorityGroup: string;
};

export type ContainerPackingFormState = {
  containerType: string;

  length: string;

  width: string;

  height: string;

  volume: string;

  maxWeight: string;
};

export type ContainerPackingRulesFormState = {
  allowRotation: boolean;

  usePalletConstraints: boolean;

  defaultPalletLength: string;

  defaultPalletWidth: string;

  defaultPalletHeight: string;

  defaultPalletWeight: string;

  enforceCenterOfGravity: boolean;

  centerOfGravityTolerancePercent: string;

  minimumSupportAreaPercent: string;

  requireSameFootprintStacking: boolean;
};

export const containerPackingZoneOptions: Array<{
  value: ContainerPackingZoneValue;
  label: string;
}> = [
  { value: "Auto", label: "自动" },

  { value: "Head", label: "柜头段" },

  { value: "Middle", label: "中段" },

  { value: "Door", label: "柜门段" },
];

export const containerPackingRenderModeOptions: Array<{
  value: ContainerPackingRenderModeValue;
  label: string;
}> = [
  { value: "OutlineOnly", label: "仅外轮廓" },

  { value: "FullGrid", label: "完整分格" },
];

export const containerPackingAutoRefreshDebounceMs = 900;


const containerPackingColorPalette = [
  "#4287f5",
  "#16a34a",
  "#f59e0b",
  "#dc2626",
  "#7c3aed",
  "#0891b2",
];

export function createContainerPackingCargoRow(
  index: number,
  overrides: Partial<ContainerPackingCargoRow> = {},
): ContainerPackingCargoRow {
  return {
    id: `cargo-${Date.now()}-${index}-${Math.random().toString(36).slice(2, 8)}`,

    name: `货物 ${index + 1}`,

    length: "60",

    width: "40",

    height: "40",

    weight: "10",

    quantity: "1",

    colorHex:
      containerPackingColorPalette[index % containerPackingColorPalette.length],

    usePallet: false,

    unitsPerPallet: "1",

    maxTopLoadWeight: "0",

    preferredZone: "Auto",

    loadSequence: String(index + 1),

    priorityGroup: "",

    ...overrides,
  };
}

export function buildContainerPackingStatusText(
  analysis: ApiContainerPackingAnalysisDto | null,

  rules: ContainerPackingRulesFormState,

  validCargoRowCount: number,

  refreshText: string,
) {
  const refreshSegment = `分析: ${refreshText}`;

  if (!analysis) {
    return `${formatPlainNumber(validCargoRowCount)} 类有效货物 | ${refreshSegment}`;
  }

  const palletText = rules.usePalletConstraints
    ? ` | 托盘: ${formatPlainNumber(analysis.packedPallets)}/${formatPlainNumber(analysis.totalPallets)}`
    : "";

  const centerText =
    analysis.packedItems.length > 0
      ? ` | 重心偏移: 长${formatPlainNumber(analysis.centerOfGravityLengthDeviationPercent)}% 宽${formatPlainNumber(analysis.centerOfGravityWidthDeviationPercent)}%${
          rules.enforceCenterOfGravity
            ? analysis.isCenterOfGravityWithinTolerance
              ? " 通过"
              : " 超限"
            : ""
        }`
      : "";

  return `货物: ${formatPlainNumber(analysis.totalPackages)}件 | 已装: ${formatPlainNumber(analysis.packedPackages)}件 | 未装: ${formatPlainNumber(
    analysis.unpackedPackages,
  )}件${palletText} | 本柜已装: ${formatPlainNumber(analysis.packedVolume)} CBM / ${formatPlainNumber(
    analysis.packedWeight,
  )} KGS | 货物总计: ${formatPlainNumber(analysis.totalVolume)} CBM / ${formatPlainNumber(
    analysis.totalWeight,
  )} KGS | 柜数: ${formatPlainNumber(analysis.estimatedContainerCount)}(体积${formatPlainNumber(
    analysis.containersNeededByVolume,
  )}/重量${formatPlainNumber(analysis.containersNeededByWeight)})${centerText} | ${refreshSegment}`;
}

export function signedArgbToColorHex(value: number) {
  const unsigned = value >>> 0;

  const rgb = unsigned & 0x00ffffff;

  return `#${rgb.toString(16).padStart(6, "0")}`;
}

export function shadeHexColor(hex: string, percent: number) {
  const normalized = hex.trim().replace(/^#/, "");

  if (!/^[0-9a-f]{6}$/i.test(normalized)) {
    return hex;
  }

  const ratio = Math.min(Math.abs(percent), 100) / 100;

  const target = percent >= 0 ? 255 : 0;

  const channels = [0, 2, 4].map((offset) => {
    const value = Number.parseInt(normalized.slice(offset, offset + 2), 16);

    return Math.round(value + (target - value) * ratio);
  });

  return `#${channels.map((channel) => channel.toString(16).padStart(2, "0")).join("")}`;
}

export function normalizeContainerPackingZone(
  value?: string,
): ContainerPackingZoneValue {
  const normalized = value?.trim().toLowerCase();

  const option = containerPackingZoneOptions.find(
    (candidate) => candidate.value.toLowerCase() === normalized,
  );

  return option?.value ?? "Auto";
}

export function findContainerType(
  containerTypes: ApiContainerTypeDto[],
  typeName: string,
) {
  const normalized = typeName.trim().toLowerCase();

  return containerTypes.find(
    (type) => type.name.trim().toLowerCase() === normalized,
  );
}

export function formatFormNumber(value?: number) {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "";
  }

  return ExactDecimal.from(value).normalize().toString();
}


export function formatPackingPercent(value?: number) {
  const formatted = formatPlainNumber(value);

  return formatted === "-" ? formatted : `${formatted}%`;
}
