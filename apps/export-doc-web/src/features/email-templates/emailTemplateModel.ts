export type EmailTemplateDraft = {
  name: string;
  category: string;
  subject: string;
  bodyHtml: string;
  isActive: boolean;
  isShared: boolean;
};

export function createEmptyEmailTemplateDraft(): EmailTemplateDraft {
  return { name: "", category: "通用", subject: "", bodyHtml: "", isActive: true, isShared: false };
}

export function areEmailTemplateDraftsEqual(left: EmailTemplateDraft, right: EmailTemplateDraft) {
  return left.name === right.name
    && left.category === right.category
    && left.subject === right.subject
    && left.bodyHtml === right.bodyHtml
    && left.isActive === right.isActive
    && left.isShared === right.isShared;
}

export function createEmailTemplateCopyName(sourceName: string, existingNames: readonly string[]) {
  const normalizedSource = sourceName.trim() || "未命名模板";
  const used = new Set(existingNames.map((item) => item.trim().toLocaleLowerCase("zh-CN")));
  const firstCandidate = `${normalizedSource} 副本`;
  if (!used.has(firstCandidate.toLocaleLowerCase("zh-CN"))) return firstCandidate;
  for (let index = 2; index < 1000; index += 1) {
    const candidate = `${firstCandidate} ${index}`;
    if (!used.has(candidate.toLocaleLowerCase("zh-CN"))) return candidate;
  }
  return `${firstCandidate} ${Date.now()}`;
}
