import { useRef, useState } from "react";
import { Download, Upload } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { currentLocalDateInputValue, readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";

export function HsKnowledgeTransfer({ client, canImport, canExport }: {
  client: ExportDocManagerApiClient; canImport: boolean; canExport: boolean;
}) {
  const input = useRef<HTMLInputElement>(null);
  const [feedback, setFeedback] = useState<{ tone: "success" | "error"; message: string } | null>(null);
  const onError = (error: unknown) => setFeedback({ tone: "error", message: readApiError(error) });
  const scope = `hs-transfer:${canImport}:${canExport}`;
  const exporter = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) => client.exportHsCodeKnowledge({}, { signal }),
    onSuccess: blob => downloadBlob(blob, `ExportDocManager-HsLibrary-${currentLocalDateInputValue().replace(/-/g, "")}.edmhs`),
    onError,
  });
  const importer = useScopedMutation(scope, client, {
    mutationFn: (file: File, signal) => client.importHsCodeKnowledge({ body: file }, { signal }),
    onSuccess: response => {
      setFeedback({ tone: "success", message: response.result.message });
    },
    refreshKeys: () => [["hs-knowledge"], ["hs-remote-candidates"], ["hs-history-candidates"], ["master-data", "hs-codes"]],
    onError,
  });
  const busy = exporter.isPending || importer.isPending;
  return <div className="knowledge-task-grid">
    <article className="knowledge-task-card">
      <Download size={24} aria-hidden="true" /><h2>导出本机知识库</h2>
      <p>包含已验证税则、正式申报实例、替代关系和学习记录，不包含业务单据和账号数据。</p>
      {canExport ? <Button busy={exporter.isPending} disabled={busy} onClick={() => { setFeedback(null); exporter.mutate(); }}>导出 .edmhs</Button> : <span className="muted-text">需要管理权限</span>}
    </article>
    <article className="knowledge-task-card">
      <Upload size={24} aria-hidden="true" /><h2>导入知识库</h2>
      <p>导入前校验完整性；缺少有效编码来源、年度或验证时间的知识包会被拒绝。</p>
      <input ref={input} hidden type="file" accept=".edmhs" onChange={event => {
        const file = event.currentTarget.files?.[0]; event.currentTarget.value = "";
        if (file && canImport && !busy) { setFeedback(null); importer.mutate(file); }
      }} />
      {canImport ? <Button variant="primary" busy={importer.isPending} disabled={busy} onClick={() => input.current?.click()}>选择知识库文件</Button> : <span className="muted-text">当前权限只能查看</span>}
    </article>
    {feedback && <InlineNotice tone={feedback.tone} className="knowledge-transfer-message">{feedback.message}</InlineNotice>}
  </div>;
}
