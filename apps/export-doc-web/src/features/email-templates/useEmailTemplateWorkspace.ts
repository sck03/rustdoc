import { useRef, useState } from "react";
import type { ApiEmailTemplateDto, ApiEmailTemplateVersionDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useAbortableOperationController } from "../../ui/useAbortableOperation.ts";
import { areEmailTemplateDraftsEqual, createEmptyEmailTemplateDraft, type EmailTemplateDraft } from "./emailTemplateModel.ts";

export function toDraft(template: ApiEmailTemplateDto): EmailTemplateDraft {
  const { name, category, subject, bodyHtml } = template;
  return { name, category, subject, bodyHtml };
}

// Collection refreshes never own the editor. Only explicit selection or a
// successful write may advance its saved baseline; later typing stays intact.
export function useEmailTemplateWorkspace(client: ExportDocManagerApiClient) {
  const [templates, setTemplates] = useState<ApiEmailTemplateDto[]>([]);
  const [selected, setSelected] = useState<ApiEmailTemplateDto>();
  const [draft, setDraft] = useState(createEmptyEmailTemplateDraft);
  const [savedDraft, setSavedDraft] = useState(createEmptyEmailTemplateDraft);
  const [versions, setVersions] = useState<ApiEmailTemplateVersionDto[]>([]);
  const [selectedVersionNumber, setSelectedVersionNumber] = useState(0);
  const [busy, setBusy] = useState(false);
  const current = useRef({ id: 0, generation: 0, pending: false, draft });
  current.current.draft = draft;
  const reads = useAbortableOperationController(client, { cancelPrevious: true });
  const history = useAbortableOperationController(client, { cancelPrevious: true });
  const writes = useAbortableOperationController(client);

  function open(template?: ApiEmailTemplateDto, initial = template ? toDraft(template) : createEmptyEmailTemplateDraft()) {
    writes.cancel(); history.cancel(); reads.cancel();
    current.current = { id: template?.id ?? 0, generation: current.current.generation + 1, pending: false, draft: initial };
    setSelected(template); setDraft(initial);
    setSavedDraft(template ? toDraft(template) : createEmptyEmailTemplateDraft());
    setVersions([]); setSelectedVersionNumber(0); setBusy(false);
  }

  async function loadTemplates(query = { keyword: "", includeArchived: false }) {
    const rows = await reads.run((signal) => client.listEmailTemplates(query, { signal }));
    setTemplates(rows);
  }

  async function loadVersions(templateId = current.current.id) {
    if (!templateId || templateId !== current.current.id) return;
    const generation = current.current.generation;
    setVersions([]); setSelectedVersionNumber(0);
    const rows = await history.run((signal) => client.listEmailTemplateVersions({ id: templateId }, { signal }));
    if (generation !== current.current.generation) return;
    setVersions(rows.filter((row) => row.emailTemplateId === templateId));
    setSelectedVersionNumber(rows[0]?.versionNumber ?? 0);
  }

  async function write(id: number, operation: (signal: AbortSignal) => Promise<ApiEmailTemplateDto>) {
    if (current.current.pending || current.current.id !== id) return undefined;
    const snapshot = current.current;
    const submitted = snapshot.draft;
    snapshot.pending = true; setBusy(true);
    try {
      const saved = await writes.run(operation);
      if (snapshot !== current.current) return undefined;
      reads.cancel(); history.cancel();
      snapshot.id = saved.id;
      setSelected(saved);
      setTemplates((rows) => [...rows.filter((row) => row.id !== saved.id), saved]);
      setSavedDraft(toDraft(saved));
      setDraft((latest) => areEmailTemplateDraftsEqual(latest, submitted) ? toDraft(saved) : latest);
      setVersions([]); setSelectedVersionNumber(0);
      return saved;
    } finally {
      if (snapshot === current.current) { snapshot.pending = false; setBusy(false); }
    }
  }

  return { templates, selected, draft, setDraft, savedDraft, versions, selectedVersionNumber, setSelectedVersionNumber, busy, open, loadTemplates, loadVersions, write };
}
