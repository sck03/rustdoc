import type {
  ReportBorderStyle,
  ReportTextStyle,
} from "./reportDesignerSchema.ts";
import { portableReportSansFontFamily } from "../../app/typographyPolicy.ts";

export type ReportDesignerSchemaIssue = {
  severity: "error" | "warning";
  path: string;
  message: string;
};

const fieldPathPattern = /^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$/;
const cssColorPattern = /^#[0-9a-fA-F]{3,8}$/;
const safeCssFontFamilyPattern = /^[A-Za-z0-9\s"',._-]+$/;
const imageDataUrlPattern = /^data:image\/(?:png|jpe?g|gif|webp|svg\+xml);base64,[A-Za-z0-9+/=\s]+$/i;
const imageRemoteUrlPattern = /^https?:\/\/[^\s"'<>]+$/i;
const imageRelativeUrlPattern = /^(?!.*(?:^|\/)\.\.(?:\/|$))(?![a-z][a-z0-9+.-]*:)[A-Za-z0-9][A-Za-z0-9._~/%+-]*$/i;

export function isReportDesignerFieldPath(value: string) {
  return fieldPathPattern.test(value.trim());
}

export function isReportDesignerImageSource(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return false;
  }

  return imageDataUrlPattern.test(trimmed) ||
    imageRemoteUrlPattern.test(trimmed) ||
    imageRelativeUrlPattern.test(trimmed);
}

export function isSafeReportDesignerCssFontFamily(value: string) {
  return safeCssFontFamilyPattern.test(value.trim());
}

export function isReportDesignerCssColor(value: string) {
  return cssColorPattern.test(value.trim());
}

export function normalizeTextStyle(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]): ReportTextStyle {
  if (value === undefined || value === null) {
    return {};
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "文本样式无效，已使用默认样式。"));
    return {};
  }

  return {
    fontSizePt: readOptionalNumber(value.fontSizePt, 10, 6, 48, `${path}.fontSizePt`, issues),
    bold: typeof value.bold === "boolean" ? value.bold : undefined,
    align: readOptionalEnum(value.align, ["Left", "Center", "Right"] as const, `${path}.align`, issues),
    marginTopMm: readOptionalNumber(value.marginTopMm, 0, 0, 80, `${path}.marginTopMm`, issues),
    marginRightMm: readOptionalNumber(value.marginRightMm, 0, 0, 80, `${path}.marginRightMm`, issues),
    marginBottomMm: readOptionalNumber(value.marginBottomMm, 0, 0, 80, `${path}.marginBottomMm`, issues),
    marginLeftMm: readOptionalNumber(value.marginLeftMm, 0, 0, 80, `${path}.marginLeftMm`, issues),
  };
}

export function normalizeBorderStyle(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]): ReportBorderStyle {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "边框样式无效，已使用默认边框。"));
    return {
      color: "#333333",
      widthPx: 1,
      top: true,
      right: true,
      bottom: true,
      left: true,
    };
  }

  const widthPx = readNumber(value.widthPx, 1, 0, 8, `${path}.widthPx`, issues);
  return {
    color: readCssColor(value.color, `${path}.color`, issues),
    widthPx,
    style: readOptionalEnum(value.style, ["Solid", "Dashed", "None"] as const, `${path}.style`, issues) ?? "Solid",
    top: readBorderSide(value.top, widthPx > 0, `${path}.top`, issues),
    right: readBorderSide(value.right, widthPx > 0, `${path}.right`, issues),
    bottom: readBorderSide(value.bottom, widthPx > 0, `${path}.bottom`, issues),
    left: readBorderSide(value.left, widthPx > 0, `${path}.left`, issues),
  };
}

export function normalizeOptionalBorderStyle(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]): ReportBorderStyle | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "边框样式无效，已忽略。"));
    return undefined;
  }

  const widthPx = readNumber(value.widthPx, 0, 0, 8, `${path}.widthPx`, issues);
  return {
    color: readCssColor(value.color, `${path}.color`, issues),
    widthPx,
    style: readOptionalEnum(value.style, ["Solid", "Dashed", "None"] as const, `${path}.style`, issues) ?? "Solid",
    top: readBorderSide(value.top, false, `${path}.top`, issues),
    right: readBorderSide(value.right, false, `${path}.right`, issues),
    bottom: readBorderSide(value.bottom, false, `${path}.bottom`, issues),
    left: readBorderSide(value.left, false, `${path}.left`, issues),
  };
}

export function readBorderSide(value: unknown, fallback: boolean, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined || value === null || value === "") {
    return fallback;
  }

  return readBoolean(value, fallback, path, issues);
}

export function readBoolean(value: unknown, fallback: boolean, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value === "boolean") {
    return value;
  }

  if (value === undefined || value === null || value === "") {
    return fallback;
  }

  issues.push(createIssue("warning", path, "布尔值无效，已使用默认值。"));
  return fallback;
}

export function readRequiredFieldPath(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  const fieldPath = typeof value === "string" ? value.trim() : "";
  if (isReportDesignerFieldPath(fieldPath)) {
    return fieldPath;
  }

  issues.push(createIssue("error", path, "字段名只能使用点分隔标识符，例如 Invoice.InvoiceNo。"));
  return "";
}

export function readOptionalFieldPath(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined || value === null || value === "") {
    return "";
  }

  return readRequiredFieldPath(value, path, issues);
}

export function readRequiredImageSource(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  const imageSource = typeof value === "string" ? value.trim() : "";
  if (isReportDesignerImageSource(imageSource)) {
    return imageSource;
  }

  issues.push(createIssue("error", path, "图片地址只允许 data:image、http(s) 或不含上级目录的相对路径。"));
  return "";
}

export function readOptionalImageSource(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined || value === null || value === "") {
    return "";
  }

  return readRequiredImageSource(value, path, issues);
}

export function readFontFamily(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value === "string" && value.trim() && isSafeReportDesignerCssFontFamily(value)) {
    return value.trim();
  }

  issues.push(createIssue("warning", path, "默认字体无效，已回退为跨平台开源字体栈。"));
  return portableReportSansFontFamily;
}

export function readCssColor(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value === "string" && isReportDesignerCssColor(value)) {
    return value.trim();
  }

  issues.push(createIssue("warning", path, "颜色值无效，已回退为 #333333。"));
  return "#333333";
}

export function readString(value: unknown, fallback: string, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value === "string") {
    return value;
  }

  issues.push(createIssue("warning", path, "文本值无效，已使用默认文本。"));
  return fallback;
}

export function readOptionalString(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (typeof value === "string") {
    return value;
  }

  issues.push(createIssue("warning", path, "文本值无效，已忽略。"));
  return undefined;
}

export function readNumber(
  value: unknown,
  fallback: number,
  min: number,
  max: number,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  const parsed = typeof value === "number" ? value : typeof value === "string" ? Number.parseFloat(value) : Number.NaN;
  if (!Number.isFinite(parsed)) {
    issues.push(createIssue("warning", path, "数字值无效，已使用默认值。"));
    return fallback;
  }

  const clamped = Math.min(max, Math.max(min, parsed));
  if (clamped !== parsed) {
    issues.push(createIssue("warning", path, `数字值超出范围，已限制在 ${min}-${max}。`));
  }

  return clamped;
}

export function readOptionalNumber(
  value: unknown,
  fallback: number,
  min: number,
  max: number,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (value === undefined || value === null || value === "") {
    return undefined;
  }

  return readNumber(value, fallback, min, max, path, issues);
}

export function readEnum<T extends string>(
  value: unknown,
  allowed: readonly T[],
  fallback: T,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): T {
  if (typeof value === "string" && (allowed as readonly string[]).includes(value)) {
    return value as T;
  }

  issues.push(createIssue("warning", path, "枚举值无效，已使用默认值。"));
  return fallback;
}

export function readOptionalEnum<T extends string>(
  value: unknown,
  allowed: readonly T[],
  path: string,
  issues: ReportDesignerSchemaIssue[],
): T | undefined {
  if (value === undefined || value === null || value === "") {
    return undefined;
  }

  if (typeof value === "string" && (allowed as readonly string[]).includes(value)) {
    return value as T;
  }

  issues.push(createIssue("warning", path, "枚举值无效，已忽略。"));
  return undefined;
}

export function normalizeId(
  value: unknown,
  fallbackPrefix: string,
  usedIds: Set<string>,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  const baseId = typeof value === "string" && value.trim()
    ? value.trim()
    : `${fallbackPrefix}-${usedIds.size + 1}`;
  let candidate = baseId;
  let suffix = 2;

  while (usedIds.has(candidate)) {
    candidate = `${baseId}-${suffix}`;
    suffix += 1;
  }

  if (candidate !== value) {
    issues.push(createIssue("warning", path, "ID 缺失或重复，已自动修正。"));
  }

  usedIds.add(candidate);
  return candidate;
}

export function createIssue(
  severity: ReportDesignerSchemaIssue["severity"],
  path: string,
  message: string,
): ReportDesignerSchemaIssue {
  return {
    severity,
    path,
    message,
  };
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
