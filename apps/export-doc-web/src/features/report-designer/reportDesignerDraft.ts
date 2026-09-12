export type ReportDesignerDraft = {
  content: string;
  isDirty: boolean;
  isValid: boolean;
};

export const EMPTY_REPORT_DESIGNER_DRAFT: ReportDesignerDraft = { content: "", isDirty: false, isValid: true };
