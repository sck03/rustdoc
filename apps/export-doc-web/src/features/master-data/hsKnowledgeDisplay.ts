const statusLabels: Record<string, string> = {
  Active: "当前有效",
  SuggestedReplacement: "待核验替代",
  WebRecommended: "网页推荐待核验",
  ObsoleteMapped: "已确认替代",
  ObsoleteUnresolved: "未找到替代",
  Ambiguous: "多条替代待选",
  ManuallyVerified: "人工已确认",
  Unresolved: "待处理",
};

export function formatHsKnowledgeStatus(status?: string) {
  return statusLabels[status ?? ""] ?? status ?? "待处理";
}

export function formatHsKnowledgeVerifiedAt(value?: string) {
  if (!value) return "未标明验证时间";
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : `验证于 ${date.toLocaleDateString("zh-CN")}`;
}
