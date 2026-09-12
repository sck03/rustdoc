import { useCallback, useState } from "react";
import type { ApiReportTemplateContentDto, ApiUserReportTemplateDto } from "../../api/index.ts";
import { EMPTY_REPORT_DESIGNER_DRAFT, type ReportDesignerDraft } from "../report-designer/reportDesignerDraft.ts";
import { hasValidReportDesignerV3Schema } from "../report-designer/reportDesignerV3TemplateParser.ts";
import { buildUserTemplateKey, type DesignerMode } from "./reportTemplateDesignerModel.ts";

type TemplateDocument = {
  path: string;
  content: string;
  baseline: string;
  name: string;
  baselineName: string;
  revision: string;
  userVersion: number;
  mode: DesignerMode;
  draft: ReportDesignerDraft;
};

const emptyDocument: TemplateDocument = {
  path: "", content: "", baseline: "", name: "", baselineName: "", revision: "", userVersion: 0,
  mode: "v3", draft: EMPTY_REPORT_DESIGNER_DRAFT,
};

function isDirty(document: TemplateDocument) {
  return document.content !== document.baseline || document.name !== document.baselineName || document.draft.isDirty;
}

export function useReportTemplateDocument() {
  const [document, setDocument] = useState(emptyDocument);
  const load = useCallback((next: TemplateDocument, force: boolean) => {
    setDocument(current => {
      if (!force && current.path === next.path && (isDirty(current) || next.userVersion < current.userVersion || (
        current.revision === next.revision && current.userVersion === next.userVersion &&
        current.baseline === next.baseline && current.baselineName === next.baselineName
      ))) return current;
      return next;
    });
  }, []);
  const loadFile = useCallback((template: ApiReportTemplateContentDto, force = false) => {
    load({
      ...emptyDocument, path: template.templatePath, content: template.content, baseline: template.content,
      name: template.displayName, baselineName: template.displayName, revision: template.revision,
      mode: hasValidReportDesignerV3Schema(template.content) ? "v3" : "advancedHtml",
    }, force);
  }, [load]);
  const loadUser = useCallback((template: ApiUserReportTemplateDto, force = false) => {
    load({
      ...emptyDocument, path: buildUserTemplateKey(template.id), content: template.contentHtml, baseline: template.contentHtml,
      name: template.name, baselineName: template.name, userVersion: template.versionNumber,
      mode: hasValidReportDesignerV3Schema(template.contentHtml) ? "v3" : "advancedHtml",
    }, force);
  }, [load]);
  const clear = useCallback(() => setDocument(emptyDocument), []);
  const setContent = useCallback((content: string) => setDocument(current => ({ ...current, content, draft: EMPTY_REPORT_DESIGNER_DRAFT })), []);
  const setName = useCallback((name: string) => setDocument(current => ({ ...current, name })), []);
  const acceptName = useCallback((name: string, revision: string) => setDocument(current => ({ ...current, name, baselineName: name, revision })), []);
  const setMode = useCallback((mode: DesignerMode) => setDocument(current => ({ ...current, mode })), []);
  const setDraft = useCallback((draft: ReportDesignerDraft) => setDocument(current =>
    current.draft.content === draft.content && current.draft.isDirty === draft.isDirty && current.draft.isValid === draft.isValid
      ? current : { ...current, draft }), []);

  return { document, hasUnsavedChanges: Boolean(document.path) && isDirty(document), loadFile, loadUser, clear, setContent, setName, acceptName, setMode, setDraft };
}
