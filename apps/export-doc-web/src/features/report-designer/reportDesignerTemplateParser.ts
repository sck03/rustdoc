import type { ReportDesignerSchema } from "./reportDesignerSchema.ts";
import {
  hasBlockingReportDesignerSchemaIssues,
  normalizeReportDesignerSchema,
} from "./reportDesignerSchemaValidation.ts";

const schemaCommentPattern = /<!--\s*EXPORTDOC_REPORT_DESIGNER_SCHEMA\s*([\s\S]*?)\s*-->/i;

export function parseReportDesignerSchemaFromHtml(content: string): ReportDesignerSchema | null {
  const match = content.match(schemaCommentPattern);
  if (!match) {
    return null;
  }

  try {
    const parsed = JSON.parse(match[1]) as unknown;
    const result = normalizeReportDesignerSchema(parsed);
    return result.schema && !hasBlockingReportDesignerSchemaIssues(result.issues) ? result.schema : null;
  } catch {
    return null;
  }
}

export function hasReportDesignerSchema(content: string) {
  return schemaCommentPattern.test(content);
}
