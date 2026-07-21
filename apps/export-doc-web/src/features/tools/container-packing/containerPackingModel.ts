import type {
  ApiContainerPackingAnalysisDto,
  ApiContainerPackingAnalyzeRequest,
  ApiContainerPackingProjectSaveRequest,
  ApiContainerTypeDto,
} from "../../../api/index.ts";
import { formatPlainNumber, readNumber } from "../../../ui/formUtils.ts";

export type ContainerPackingZoneValue = "Auto" | "Head" | "Middle" | "Door";

export type ContainerPackingRenderModeValue = "OutlineOnly" | "FullGrid";

export type ContainerPackingAnalyzeMode = "manual" | "auto";

export type ContainerPackingAnalyzeVariables = {
  mode: ContainerPackingAnalyzeMode;

  request: ApiContainerPackingAnalyzeRequest;

  sequence: number;

  signature: string;
};

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

const maxContainerPackingGridSegments = 18;

export const containerPackingColorPalette = [
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

export function buildContainerPackingAnalyzeRequest(
  container: ContainerPackingFormState,

  cargoRows: ContainerPackingCargoRow[],

  rules: ContainerPackingRulesFormState,
): ApiContainerPackingAnalyzeRequest {
  return {
    container: {
      length: readPositiveIntegerInput(container.length, 0),

      width: readPositiveIntegerInput(container.width, 0),

      height: readPositiveIntegerInput(container.height, 0),

      volume: readNonNegativeNumberInput(container.volume),

      maxWeight: readNonNegativeNumberInput(container.maxWeight),
    },

    cargoItems: cargoRows
      .filter(isValidContainerPackingCargoRow)
      .map((row, index) => ({
        name: row.name.trim() || `货物 ${index + 1}`,

        length: readPositiveNumberInput(row.length),

        width: readPositiveNumberInput(row.width),

        height: readPositiveNumberInput(row.height),

        weight: readNonNegativeNumberInput(row.weight),

        quantity: readPositiveIntegerInput(row.quantity, 1),

        colorArgb: colorHexToSignedArgb(row.colorHex),

        usePallet: row.usePallet,

        unitsPerPallet: readPositiveIntegerInput(row.unitsPerPallet, 1),

        maxTopLoadWeight: readNonNegativeNumberInput(row.maxTopLoadWeight),

        preferredZone: row.preferredZone,

        loadSequence: readPositiveIntegerInput(row.loadSequence, index + 1),

        priorityGroup: row.priorityGroup.trim(),
      })),

    rules: {
      allowRotation: rules.allowRotation,

      usePalletConstraints: rules.usePalletConstraints,

      defaultPalletLength: readPositiveIntegerInput(
        rules.defaultPalletLength,
        120,
      ),

      defaultPalletWidth: readPositiveIntegerInput(
        rules.defaultPalletWidth,
        100,
      ),

      defaultPalletHeight: readPositiveIntegerInput(
        rules.defaultPalletHeight,
        15,
      ),

      defaultPalletWeight: readNonNegativeNumberInput(
        rules.defaultPalletWeight,
      ),

      enforceCenterOfGravity: rules.enforceCenterOfGravity,

      centerOfGravityTolerancePercent: readNonNegativeNumberInput(
        rules.centerOfGravityTolerancePercent,
      ),

      minimumSupportAreaPercent: readNonNegativeNumberInput(
        rules.minimumSupportAreaPercent,
      ),

      requireSameFootprintStacking: rules.requireSameFootprintStacking,
    },
  };
}

export function buildContainerPackingProjectSaveRequest(
  projectId: number,

  expectedVersion: number,

  projectName: string,

  container: ContainerPackingFormState,

  cargoRows: ContainerPackingCargoRow[],

  rules: ContainerPackingRulesFormState,
): ApiContainerPackingProjectSaveRequest {
  const analysisRequest = buildContainerPackingAnalyzeRequest(
    container,
    cargoRows,
    rules,
  );

  return {
    id: Math.max(projectId, 0),

    expectedVersion: Math.max(expectedVersion, 0),

    name: projectName.trim(),

    containerType: container.containerType.trim(),

    container: analysisRequest.container,

    rules: analysisRequest.rules,

    cargoItems: analysisRequest.cargoItems,
  };
}

export function isValidContainerPackingCargoRow(row: ContainerPackingCargoRow) {
  return (
    readPositiveNumberInput(row.length) > 0 &&
    readPositiveNumberInput(row.width) > 0 &&
    readPositiveNumberInput(row.height) > 0 &&
    readPositiveIntegerInput(row.quantity, 0) > 0
  );
}

export function readPositiveNumberInput(value: string, fallback = 0) {
  const parsed = readNumber(value);

  return parsed > 0 ? parsed : fallback;
}

export function readNonNegativeNumberInput(value: string, fallback = 0) {
  const parsed = readNumber(value);

  return parsed >= 0 ? parsed : fallback;
}

export function readPositiveIntegerInput(value: string, fallback = 1) {
  const parsed = Math.trunc(readNumber(value));

  return parsed > 0 ? parsed : fallback;
}

export function colorHexToSignedArgb(hex: string) {
  const normalized = hex.trim().replace(/^#/, "");

  if (!/^[0-9a-f]{6}$/i.test(normalized)) {
    return colorHexToSignedArgb(containerPackingColorPalette[0]);
  }

  const unsigned = Number.parseInt(`ff${normalized}`, 16);

  return unsigned > 0x7fffffff ? unsigned - 0x100000000 : unsigned;
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

  return Number.isInteger(value)
    ? String(value)
    : String(Number(value.toFixed(3)));
}

function formatContainerPackingZone(value?: string) {
  const normalized = value?.trim().toLowerCase();

  return (
    containerPackingZoneOptions.find(
      (option) => option.value.toLowerCase() === normalized,
    )?.label ??
    value ??
    "-"
  );
}

export function formatPackingPercent(value?: number) {
  const formatted = formatPlainNumber(value);

  return formatted === "-" ? formatted : `${formatted}%`;
}

export function formatPackingItemFlags(
  isRotated: boolean,
  isPalletized: boolean,
) {
  const flags = [isRotated ? "旋转" : "", isPalletized ? "托盘" : ""].filter(
    Boolean,
  );

  return flags.length > 0 ? flags.join(" / ") : "-";
}
