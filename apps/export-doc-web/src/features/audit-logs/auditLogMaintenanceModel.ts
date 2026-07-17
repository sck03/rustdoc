export type AuditLogFilters = {
  invoiceKeyword: string;
  entityName: string;
  action: string;
  userId: string;
  keyword: string;
  startTime: string;
  endTime: string;
};

export type AuditLogFilterLabels = {
  entityLabels?: Record<string, string>;
  actionLabels?: Record<string, string>;
};

export function hasActiveAuditLogFilters(filters: AuditLogFilters) {
  return Object.values(filters).some((value) => Boolean(value.trim()));
}

export function describeAuditLogFilters(filters: AuditLogFilters, labels: AuditLogFilterLabels = {}) {
  const descriptions: string[] = [];
  appendDescription(descriptions, filters.invoiceKeyword, (value) => `发票包含“${value}”`);
  appendDescription(descriptions, filters.entityName, (value) => `实体为“${labels.entityLabels?.[value] || value}”`);
  appendDescription(descriptions, filters.action, (value) => `动作为“${labels.actionLabels?.[value] || value}”`);
  appendDescription(descriptions, filters.userId, (value) => `操作人包含“${value}”`);
  appendDescription(descriptions, filters.keyword, (value) => `关键字包含“${value}”`);

  if (filters.startTime && filters.endTime) {
    descriptions.push(`时间从 ${formatFilterDateTime(filters.startTime)} 到 ${formatFilterDateTime(filters.endTime)}`);
  } else if (filters.startTime) {
    descriptions.push(`时间不早于 ${formatFilterDateTime(filters.startTime)}`);
  } else if (filters.endTime) {
    descriptions.push(`时间不晚于 ${formatFilterDateTime(filters.endTime)}`);
  }

  return descriptions;
}

function appendDescription(target: string[], value: string, format: (normalizedValue: string) => string) {
  const normalized = value.trim();
  if (normalized) {
    target.push(format(normalized));
  }
}

function formatFilterDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString("zh-CN", {
        hour12: false,
      });
}
