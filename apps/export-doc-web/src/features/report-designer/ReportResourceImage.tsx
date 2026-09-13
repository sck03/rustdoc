import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { useBlobUrl } from "../../ui/useBlobUrl.ts";
import { usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import "../../styles/report/designer-v3-images.css";

export function ReportResourceImage({ client, resourceId, alt }: { client?: ExportDocManagerApiClient; resourceId?: string; alt: string }) {
  const canView = usePermission(permissionResources.reportResources, permissionActions.view).allowed;
  if (!client || !resourceId || !canView) return <span className="report-designer-v3-preview-image">{resourceId ? "图片预览不可用" : "图片资源未上传"}</span>;
  return <LoadedResourceImage client={client} resourceId={resourceId} alt={alt} />;
}

function LoadedResourceImage({ client, resourceId, alt }: { client: ExportDocManagerApiClient; resourceId: string; alt: string }) {
  const query = useQuery({
    queryKey: ["report-resource-image", resourceId],
    queryFn: ({ signal }) => client.downloadReportTemplateV3ImageResource({ resourceId }, { signal }),
    staleTime: 60_000,
    gcTime: 0,
    retry: false,
  });
  const url = useBlobUrl(query.data ?? null);
  const [failedUrl, setFailedUrl] = useState("");
  if (query.isError || url && failedUrl === url) return <span className="report-designer-v3-preview-image" title="图片不可用或访问权限已改变">图片无法显示</span>;
  if (!url) return <span className="report-designer-v3-preview-image" role="status">正在读取图片…</span>;
  return <img className="report-designer-v3-resource-image" src={url} alt={alt || "模板图片"} draggable={false} onError={() => setFailedUrl(url)} />;
}
