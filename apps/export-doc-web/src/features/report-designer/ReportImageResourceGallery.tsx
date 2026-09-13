import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiReportTemplateImageResourceResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useAbortableOperation, isAbortError } from "../../ui/useAbortableOperation.ts";
import { ReportResourceImage } from "./ReportResourceImage.tsx";
import type { ReportDesignerV3ImageResource } from "./reportDesignerV3Schema.ts";

export function ReportImageResourceGallery({ client, editable, resources, onChoose }: {
  client: ExportDocManagerApiClient;
  editable: boolean;
  resources: ReportDesignerV3ImageResource[];
  onChoose: (resource: ApiReportTemplateImageResourceResponse) => void;
}) {
  const [open, setOpen] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);
  const [busyId, setBusyId] = useState("");
  const [feedback, setFeedback] = useState("");
  const confirmation = useConfirmation();
  const run = useAbortableOperation();
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: ["report-image-resources", pageNumber, resources.length],
    queryFn: ({ signal }) => client.queryReportTemplateV3ImageResources({ pageNumber, pageSize: 12 }, { signal }),
    enabled: open,
    staleTime: 0,
  });
  useEffect(() => {
    if (query.data && pageNumber > Math.max(1, query.data.totalPages)) setPageNumber(Math.max(1, query.data.totalPages));
  }, [pageNumber, query.data]);
  async function recycle(id: string) {
    if (!editable || busyId || !await confirmation({ title: "回收未使用图片", description: "确定回收这张图片吗？", details: ["服务端会再次检查所有模板和历史版本的引用。", "其他上传者持有的图片仍会保留。"], confirmLabel: "确认回收", tone: "danger" })) return;
    setBusyId(id);
    setFeedback("");
    try {
      const result = await run(signal => client.recycleReportTemplateV3ImageResource({ resourceId: id }, { signal }));
      setFeedback(result.message);
      await queryClient.invalidateQueries({ queryKey: ["report-image-resources"] });
      queryClient.removeQueries({ queryKey: ["report-resource-image", id] });
    } catch (error) { if (!isAbortError(error)) setFeedback(readApiError(error)); }
    finally { setBusyId(""); }
  }
  return <details className="report-designer-v3-resource-gallery" open={open} onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>图片资源库</summary>
    {open ? <>
      <p className="report-designer-v3-muted">选择图片即可复用。当前草稿包含的图片、已保存模板及历史版本使用的图片均保留。</p>
      {query.isPending ? <p role="status">正在读取图片资源…</p> : null}
      {query.isError ? <div role="alert">{readApiError(query.error)}<button type="button" className="command-button secondary" onClick={() => void query.refetch()}>重新读取</button></div> : null}
      {query.data?.items.map(resource => {
        const inDraft = resources.some(item => item.id === resource.id);
        return <article key={resource.id} className="report-designer-v3-resource-card">
          <div className="report-designer-v3-resource-thumbnail"><ReportResourceImage client={client} resourceId={resource.id} alt="可复用图片" /></div>
          <small>{Math.ceil(resource.byteLength / 1024)} KB · {inDraft ? "当前草稿包含" : resource.isReferenced ? "模板或历史版本在用" : "未被已保存模板使用"}</small>
          <div className="report-designer-v3-resource-card-actions">
            <button type="button" className="command-button secondary" disabled={!editable || Boolean(busyId)} onClick={() => onChoose({ ...resource, altText: "模板图片", storagePolicy: "" })}>使用图片</button>
            {resource.canRecycle && !inDraft ? <button type="button" className="command-button secondary danger-button" disabled={!editable || Boolean(busyId)} onClick={() => void recycle(resource.id)}>{busyId === resource.id ? "回收中…" : "回收"}</button> : null}
          </div>
        </article>;
      })}
      {query.data && query.data.totalCount === 0 ? <p>还没有可复用的图片，请先上传。</p> : null}
      {query.data && query.data.totalPages > 1 ? <div className="report-designer-v3-resource-card-actions">
        <button type="button" className="command-button secondary" disabled={!query.data.hasPreviousPage || query.isFetching} onClick={() => setPageNumber(value => value - 1)}>上一页</button>
        <span>{pageNumber} / {query.data.totalPages}</span>
        <button type="button" className="command-button secondary" disabled={!query.data.hasNextPage || query.isFetching} onClick={() => setPageNumber(value => value + 1)}>下一页</button>
      </div> : null}
      {feedback ? <p role="status">{feedback}</p> : null}
    </> : null}
  </details>;
}
