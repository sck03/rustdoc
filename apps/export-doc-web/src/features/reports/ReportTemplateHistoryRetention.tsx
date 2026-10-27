import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiTemplateHistoryCleanupPreview, ApiUserReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { ConfirmationDialog } from "../../ui/ConfirmationDialog.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";

const megabytes = (bytes: number) => `${(bytes / 1024 / 1024).toFixed(2)} MiB`;

export function ReportTemplateHistoryRetention({ client, template, enabled, isBusy }: {
  client: ExportDocManagerApiClient; template: ApiUserReportTemplateDto; enabled: boolean; isBusy: boolean;
}) {
  const queries = useQueryClient();
  const [retain, setRetain] = useState(20);
  const [preview, setPreview] = useState<ApiTemplateHistoryCleanupPreview | null>(null);
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const save = useScopedSave(`${template.id}:${template.versionNumber}:${retain}`, client);
  const usage = useQuery({ queryKey: [...queryKeys.userReportTemplateVersions(template.id), "retention", template.versionNumber, retain],
    queryFn: ({ signal }) => client.getUserReportTemplateHistoryRetention({ id: template.id, retainLatest: retain }, { signal }),
    enabled, staleTime: 30_000 });
  useEffect(() => { setPreview(null); setReason(""); }, [template.id, template.versionNumber, retain]);
  useUnsavedChangesGuard({ isDirty: Boolean(preview && reason), message: "模板历史清理的原因尚未提交。" });
  async function run(commit: boolean) {
    let saved = false;
    try {
      await save.run(async ({ signal, checkCurrent }) => {
        setError(""); setMessage("");
        if (!commit) {
          const result = await client.previewUserReportTemplateHistoryCleanup({ id: template.id, body: { retainLatest: retain } }, { signal });
          checkCurrent(); setPreview(result); setReason(""); return;
        }
        if (!preview || !reason.trim()) return;
        const response = await client.commitUserReportTemplateHistoryCleanup({ id: template.id, body: { token: preview.token, reason: reason.trim() } }, { signal });
        checkCurrent(); saved = true; setPreview(null); setReason(""); setMessage(response.message);
        await queries.invalidateQueries({ queryKey: queryKeys.userReportTemplateVersions(template.id) });
      });
    } catch (failure) {
      if (!isAbortError(failure)) setError(`${saved ? "历史已清理，但刷新失败：" : ""}${readApiError(failure)}`);
    }
  }
  return <div className="template-inline-details">
    {usage.data && <small>历史快照：{usage.data.totalCount} 版 · {megabytes(usage.data.totalBytes)}；按当前保留数量可清理 {usage.data.eligibleCount} 版 · {megabytes(usage.data.eligibleBytes)}。</small>}
    {usage.isError && <InlineNotice tone="error">{readApiError(usage.error)}<button type="button" onClick={() => void usage.refetch()}>重新读取容量</button></InlineNotice>}
    <label><span>保留最近版本数</span><select aria-label="历史保留版本数" value={retain} disabled={isBusy || save.isSaving} onChange={event => setRetain(Number(event.target.value))}>
      {[5, 10, 20, 50, 100, 200].map(count => <option key={count} value={count}>{count} 版</option>)}
    </select></label>
    {usage.data && <small>{usage.data.policy}</small>}
    <button className="command-button secondary compact-button" type="button" disabled={isBusy || save.isSaving || !usage.data?.canClean || !usage.data.eligibleCount} onClick={() => void run(false)}>预检旧快照清理</button>
    {message && <InlineNotice tone="success">{message}</InlineNotice>}{error && <InlineNotice tone="error">{error}</InlineNotice>}
    {preview && <ConfirmationDialog title="清理模板历史快照" description={`本批清理 ${preview.items.length} 版旧快照，约 ${megabytes(preview.items.reduce((sum, item) => sum + item.byteLength, 0))}，保留最近 ${preview.retention.retainLatest} 版。`}
      details={["删除的快照将不能恢复；当前模板和业务审计继续保留。", `本批版本：${preview.items.slice(0, 20).map(item => `V${item.versionNumber}`).join("、")}${preview.items.length > 20 ? "等" : ""}`]}
      confirmLabel="确认清理旧快照" confirmDisabled={!reason.trim() || !preview.items.length} isBusy={save.isSaving}
      onCancel={() => { setPreview(null); setReason(""); }} onConfirm={() => void run(true)}>
      <label className="confirmation-dialog-details"><span>清理原因</span><textarea aria-label="历史清理原因" rows={3} maxLength={1000} value={reason} disabled={save.isSaving} onChange={event => setReason(event.target.value)} /></label>
    </ConfirmationDialog>}
  </div>;
}
