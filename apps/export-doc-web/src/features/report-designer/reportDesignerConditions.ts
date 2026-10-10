import { isCalendarDate } from "../../ui/calendarDate.ts";
import { createIssue, isRecord, readRequiredFieldPath, readString, type ReportDesignerSchemaIssue } from "./reportDesignerSchemaValues.ts";

export const MAX_CONDITIONAL_RULES = 8;
export const conditionOperators = ["HasValue", "IsEmpty", "Equals", "NotEquals", "Contains", "NotContains", "StartsWith", "EndsWith", "GreaterThan", "GreaterOrEqual", "LessThan", "LessOrEqual"] as const;
export const comparisonTypes = ["Text", "Number", "Date"] as const;
export const conditionMatchModes = ["All", "Any"] as const;
export type ReportComparisonType = typeof comparisonTypes[number];
export type ReportConditionMatch = typeof conditionMatchModes[number];
export type ReportConditionalRule = {
  fieldPath: string;
  operator: typeof conditionOperators[number];
  value: string;
  comparisonType?: ReportComparisonType;
  ignoreCase?: boolean;
};

const textOperators = ["Contains", "NotContains", "StartsWith", "EndsWith"];
const orderOperators = ["GreaterThan", "GreaterOrEqual", "LessThan", "LessOrEqual"];
const operatorLabels = {
  HasValue: "有内容（非空白）", IsEmpty: "为空", Equals: "等于", NotEquals: "不等于",
  Contains: "包含", NotContains: "不包含", StartsWith: "开头是", EndsWith: "结尾是",
  GreaterThan: "大于", GreaterOrEqual: "大于或等于", LessThan: "小于", LessOrEqual: "小于或等于",
};
const dateLabels = { GreaterThan: "晚于", GreaterOrEqual: "不早于", LessThan: "早于", LessOrEqual: "不晚于" };

export function conditionNeedsValue(rule: ReportConditionalRule) {
  return rule.operator !== "HasValue" && rule.operator !== "IsEmpty";
}
export function conditionOperatorOptions(type: ReportComparisonType = "Text") {
  return conditionOperators.filter(operator => !(type === "Text" ? orderOperators : textOperators).includes(operator))
    .map(value => ({ value, label: type === "Date" && value in dateLabels ? dateLabels[value as keyof typeof dateLabels] : operatorLabels[value] }));
}
export function conditionRules(block: { condition: ReportConditionalRule; additionalConditions?: ReportConditionalRule[] }) {
  return [block.condition, ...(block.additionalConditions ?? [])];
}
export function conditionRuleIssue(rule: ReportConditionalRule): string | undefined {
  const type = rule.comparisonType ?? "Text";
  if (rule.fieldPath.startsWith("item.")) return "条件组件使用单据字段，不能在商品循环外判断 item 字段。";
  if ([...rule.value].length > 2048) return "比较值不能超过 2048 个字符。";
  if (rule.ignoreCase && type !== "Text") return "只有文本条件支持忽略大小写。";
  if (!conditionNeedsValue(rule)) return;
  if (!conditionOperatorOptions(type).some(option => option.value === rule.operator)) return "判断方式与比较类型不匹配。";
  if (type === "Text" && textOperators.includes(rule.operator) && !rule.value.trim()) return "文本匹配的比较值不能为空白。";
  if (type === "Date" && !isCalendarDate(rule.value)) return "日期须为有效的 YYYY-MM-DD。";
  if (type === "Number") {
    const value = rule.value.trim();
    if (!/^[+-]?\d+(?:\.\d{1,28})?$/.test(value)) return "请输入精确小数（最多 28 位小数），不含千位分隔符或单位。";
    if (BigInt(value.replace(/^[+-]/, "").replace(".", "")) > 79228162514264337593543950335n) return "数字超出精确小数支持范围。";
  }
}

function strictEnum<T extends string>(value: unknown, allowed: readonly T[], fallback: T, path: string, issues: ReportDesignerSchemaIssue[], optional = false): T {
  if (optional && value === undefined) return fallback;
  if (typeof value === "string" && (allowed as readonly string[]).includes(value)) return value as T;
  issues.push(createIssue("error", path, "条件选项无效，请重新选择。"));
  return fallback;
}

function normalizeCondition(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]): ReportConditionalRule {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "条件规则无效。"));
    return { fieldPath: "", operator: "HasValue", value: "" };
  }
  for (const key of Object.keys(value)) if (!["fieldPath", "operator", "value", "comparisonType", "ignoreCase"].includes(key)) {
    issues.push(createIssue("error", `${path}.${key}`, "条件规则包含不支持的设置。"));
  }
  if (value.ignoreCase !== undefined && typeof value.ignoreCase !== "boolean") issues.push(createIssue("error", `${path}.ignoreCase`, "忽略大小写必须为勾选值。"));
  if (value.value !== undefined && typeof value.value !== "string") issues.push(createIssue("error", `${path}.value`, "比较值必须为文本，请重新填写。"));
  const rule: ReportConditionalRule = {
    fieldPath: readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    operator: strictEnum(value.operator, conditionOperators, "HasValue", `${path}.operator`, issues),
    value: readString(value.value, "", `${path}.value`, issues),
    comparisonType: strictEnum(value.comparisonType, comparisonTypes, "Text", `${path}.comparisonType`, issues, true),
    ignoreCase: value.ignoreCase === true,
  };
  const message = conditionRuleIssue(rule);
  if (message) issues.push(createIssue("error", path, message));
  return rule;
}

export function normalizeConditions(value: Record<string, unknown>, path: string, issues: ReportDesignerSchemaIssue[]) {
  const extra = value.additionalConditions;
  if (extra !== undefined && !Array.isArray(extra)) issues.push(createIssue("error", `${path}.additionalConditions`, "附加条件必须为数组。"));
  if (Array.isArray(extra) && extra.length >= MAX_CONDITIONAL_RULES) issues.push(createIssue("error", path, `每个条件组件最多 ${MAX_CONDITIONAL_RULES} 条规则。`));
  return {
    condition: normalizeCondition(value.condition, `${path}.condition`, issues),
    additionalConditions: Array.isArray(extra) ? extra.slice(0, MAX_CONDITIONAL_RULES - 1).map((rule, index) => normalizeCondition(rule, `${path}.additionalConditions[${index}]`, issues)) : [],
    matchMode: strictEnum(value.matchMode, conditionMatchModes, "All", `${path}.matchMode`, issues, true),
  };
}
